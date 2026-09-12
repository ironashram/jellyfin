using System;
using System.IO;
using Jellyfin.Api.Helpers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Dlna;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Helpers
{
    public sealed class DeviceProfileOverridesTests : IDisposable
    {
        private readonly string _configDir = Path.Combine(Path.GetTempPath(), "jellyfin-overrides-" + Guid.NewGuid().ToString("N"));

        public DeviceProfileOverridesTests()
        {
            Directory.CreateDirectory(_configDir);
        }

        public void Dispose()
        {
            Directory.Delete(_configDir, true);
        }

        private DeviceProfileOverrides CreateOverrides(string? json)
        {
            if (json is not null)
            {
                File.WriteAllText(Path.Combine(_configDir, "device-profile-overrides.json"), json);
            }

            var configurationManager = new Mock<IServerConfigurationManager>();
            configurationManager
                .Setup(x => x.CommonApplicationPaths)
                .Returns(Mock.Of<IApplicationPaths>(p => p.ConfigurationDirectoryPath == _configDir));
            return new DeviceProfileOverrides(configurationManager.Object, Mock.Of<ILogger<DeviceProfileOverrides>>());
        }

        private static DeviceProfile CreateProfile()
        {
            return new DeviceProfile
            {
                DirectPlayProfiles =
                [
                    new DirectPlayProfile { Type = DlnaProfileType.Video, Container = "mkv", VideoCodec = "hevc,h264", AudioCodec = "aac,ac3,eac3,truehd" },
                    new DirectPlayProfile { Type = DlnaProfileType.Audio, AudioCodec = "truehd" },
                    new DirectPlayProfile { Type = DlnaProfileType.Video, Container = "mp4" }
                ],
                TranscodingProfiles =
                [
                    new TranscodingProfile { Type = DlnaProfileType.Video, Container = "mp4", VideoCodec = "hevc,h264", AudioCodec = "aac,ac3,eac3,truehd" }
                ]
            };
        }

        [Fact]
        public void Apply_MatchingDevice_RemovesAndReordersCodecs()
        {
            const string Json = """
                {"Overrides": [{"DeviceId": "tv-1", "RemoveAudioCodecs": ["truehd"], "PreferAudioCodecs": ["eac3", "ac3"]}]}
                """;
            var profile = CreateOverrides(Json).Apply(CreateProfile(), "tv-1", "Some Client");

            Assert.Equal("aac,ac3,eac3", profile.DirectPlayProfiles[0].AudioCodec);
            Assert.Equal("none", profile.DirectPlayProfiles[1].AudioCodec);
            Assert.Equal("-truehd", profile.DirectPlayProfiles[2].AudioCodec);
            Assert.Equal("hevc,h264", profile.DirectPlayProfiles[0].VideoCodec);
            Assert.Equal("eac3,ac3,aac", profile.TranscodingProfiles[0].AudioCodec);
        }

        [Fact]
        public void Apply_OtherDevice_LeavesProfileUntouched()
        {
            const string Json = """
                {"Overrides": [{"DeviceId": "tv-1", "Client": "Some Client", "RemoveAudioCodecs": ["truehd"]}]}
                """;
            var profile = CreateOverrides(Json).Apply(CreateProfile(), "tv-1", "Other Client");

            Assert.Equal("aac,ac3,eac3,truehd", profile.DirectPlayProfiles[0].AudioCodec);
            Assert.Equal("aac,ac3,eac3,truehd", profile.TranscodingProfiles[0].AudioCodec);
        }

        [Fact]
        public void Apply_NoFile_LeavesProfileUntouched()
        {
            var profile = CreateOverrides(null).Apply(CreateProfile(), "tv-1", "Some Client");

            Assert.Equal("aac,ac3,eac3,truehd", profile.DirectPlayProfiles[0].AudioCodec);
        }
    }
}
