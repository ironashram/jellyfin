using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Jellyfin.Extensions.Json;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Dlna;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Helpers;

/// <summary>
/// Applies server side overrides to the device profile a client sends with a playback request.
/// Overrides are read from device-profile-overrides.json in the configuration directory and
/// reloaded whenever the file changes.
/// </summary>
public class DeviceProfileOverrides
{
    private const string FileName = "device-profile-overrides.json";
    private const string NoCodec = "none";

    private readonly string _path;
    private readonly ILogger<DeviceProfileOverrides> _logger;
    private readonly Lock _lock = new();
    private DateTime _loadedWriteTime = DateTime.MinValue;
    private DeviceProfileOverride[] _entries = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceProfileOverrides"/> class.
    /// </summary>
    /// <param name="configurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{DeviceProfileOverrides}"/> interface.</param>
    public DeviceProfileOverrides(IServerConfigurationManager configurationManager, ILogger<DeviceProfileOverrides> logger)
    {
        _path = Path.Combine(configurationManager.CommonApplicationPaths.ConfigurationDirectoryPath, FileName);
        _logger = logger;
    }

    /// <summary>
    /// Applies every override matching the requesting device to the profile, in file order.
    /// </summary>
    /// <param name="profile">The device profile sent by the client.</param>
    /// <param name="deviceId">The device id of the requesting client.</param>
    /// <param name="client">The client name of the requesting client.</param>
    /// <returns>The same profile instance, modified in place.</returns>
    public DeviceProfile Apply(DeviceProfile profile, string? deviceId, string? client)
    {
        var entries = Load();
        if (entries.Length > 0)
        {
            _logger.LogInformation("Device profile overrides: checking {Client} ({DeviceId}) against {Count} entries", client, deviceId, entries.Length);
        }

        foreach (var entry in entries)
        {
            if (!entry.Matches(deviceId, client))
            {
                continue;
            }

            foreach (var directPlay in profile.DirectPlayProfiles)
            {
                directPlay.AudioCodec = Remove(directPlay.AudioCodec, entry.RemoveAudioCodecs);
                directPlay.VideoCodec = Remove(directPlay.VideoCodec, entry.RemoveVideoCodecs);
            }

            foreach (var transcoding in profile.TranscodingProfiles)
            {
                transcoding.AudioCodec = Prefer(Remove(transcoding.AudioCodec, entry.RemoveAudioCodecs), entry.PreferAudioCodecs);
                transcoding.VideoCodec = Remove(transcoding.VideoCodec, entry.RemoveVideoCodecs) ?? string.Empty;
            }

            _logger.LogInformation(
                "Device profile override applied to {Client} ({DeviceId}): removed audio {RemovedAudio}, removed video {RemovedVideo}, preferred audio {PreferredAudio}",
                client,
                deviceId,
                entry.RemoveAudioCodecs,
                entry.RemoveVideoCodecs,
                entry.PreferAudioCodecs);
        }

        return profile;
    }

    private DeviceProfileOverride[] Load()
    {
        lock (_lock)
        {
            var writeTime = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
            if (writeTime == _loadedWriteTime)
            {
                return _entries;
            }

            _loadedWriteTime = writeTime;
            _entries = [];
            if (writeTime == DateTime.MinValue)
            {
                return _entries;
            }

            try
            {
                using var stream = File.OpenRead(_path);
                var file = JsonSerializer.Deserialize<DeviceProfileOverridesFile>(stream, JsonDefaults.Options);
                _entries = file?.Overrides ?? [];
                _logger.LogInformation("Loaded {Count} device profile overrides from {Path}", _entries.Length, _path);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _logger.LogError(ex, "Failed to read device profile overrides from {Path}", _path);
            }

            return _entries;
        }
    }

    /// <summary>
    /// Removes codecs from a comma separated codec list. An empty list means every codec, so it becomes a
    /// negative list of the removed codecs. A negative list gets the removed codecs appended. A positive
    /// list that ends up empty becomes a value no codec can match.
    /// </summary>
    private static string? Remove(string? codecs, string[] remove)
    {
        if (remove.Length == 0)
        {
            return codecs;
        }

        if (string.IsNullOrEmpty(codecs))
        {
            return "-" + string.Join(',', remove);
        }

        if (codecs.StartsWith('-'))
        {
            var negative = Split(codecs[1..]);
            return "-" + string.Join(',', negative.Union(remove, StringComparer.OrdinalIgnoreCase));
        }

        var kept = Split(codecs).Where(c => !remove.Contains(c, StringComparer.OrdinalIgnoreCase)).ToArray();
        return kept.Length == 0 ? NoCodec : string.Join(',', kept);
    }

    /// <summary>
    /// Moves the preferred codecs that are present in a positive codec list to its front, keeping their given order.
    /// </summary>
    private static string Prefer(string? codecs, string[] prefer)
    {
        if (prefer.Length == 0 || string.IsNullOrEmpty(codecs) || codecs.StartsWith('-'))
        {
            return codecs ?? string.Empty;
        }

        var present = Split(codecs);
        var first = prefer.Where(p => present.Contains(p, StringComparer.OrdinalIgnoreCase));
        var rest = present.Where(c => !prefer.Contains(c, StringComparer.OrdinalIgnoreCase));
        return string.Join(',', first.Concat(rest));
    }

    private static string[] Split(string codecs)
        => codecs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// The on-disk shape of device-profile-overrides.json.
    /// </summary>
    internal sealed class DeviceProfileOverridesFile
    {
        /// <summary>
        /// Gets or sets the overrides, applied in order.
        /// </summary>
        public DeviceProfileOverride[] Overrides { get; set; } = [];
    }

    /// <summary>
    /// One override entry. Every selector that is set must match for the entry to apply, an entry without
    /// selectors applies to every client.
    /// </summary>
    internal sealed class DeviceProfileOverride
    {
        /// <summary>
        /// Gets or sets the device id to match.
        /// </summary>
        public string? DeviceId { get; set; }

        /// <summary>
        /// Gets or sets the client name to match, as sent in the authorization header.
        /// </summary>
        public string? Client { get; set; }

        /// <summary>
        /// Gets or sets the audio codecs to strip from the direct play and transcoding profiles.
        /// </summary>
        public string[] RemoveAudioCodecs { get; set; } = [];

        /// <summary>
        /// Gets or sets the video codecs to strip from the direct play and transcoding profiles.
        /// </summary>
        public string[] RemoveVideoCodecs { get; set; } = [];

        /// <summary>
        /// Gets or sets the audio codecs to move to the front of the transcoding profiles, so the server encodes to them first.
        /// </summary>
        public string[] PreferAudioCodecs { get; set; } = [];

        /// <summary>
        /// Returns whether this entry applies to the given client.
        /// </summary>
        /// <param name="deviceId">The device id of the requesting client.</param>
        /// <param name="client">The client name of the requesting client.</param>
        /// <returns>True if the entry applies.</returns>
        public bool Matches(string? deviceId, string? client)
            => (string.IsNullOrEmpty(DeviceId) || string.Equals(DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
               && (string.IsNullOrEmpty(Client) || string.Equals(Client, client, StringComparison.OrdinalIgnoreCase));
    }
}
