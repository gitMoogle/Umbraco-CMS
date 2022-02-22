using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Umbraco.Cms.Core.Configuration;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Manifest;
using Umbraco.Cms.Core.Semver;
using Umbraco.Cms.Core.Telemetry;
using Umbraco.Cms.Core.Telemetry.DataCollectors;
using Umbraco.Cms.Core.Telemetry.Models;

namespace Umbraco.Cms.Tests.UnitTests.Umbraco.Core.Telemetry
{
    [TestFixture]
    public class TelemetryServiceTests
    {
        [Test]
        public void CanCollectMetadataTelemetryData()
        {
            var telemetryOptions = CreateTelemetrySettings(TelemetryLevel.Basic);
            var globalSettings = Mock.Of<IOptionsMonitor<GlobalSettings>>(x => x.CurrentValue == new GlobalSettings
            {
                Id = "0f1785c5-7ba0-4c52-ab62-863bd2c8f3fe"
            });
            var telemetryDataCollectors = new[]
            {
                new MetadataTelemetryDataCollector(globalSettings)
            };

            var telemetryService = new TelemetryService(telemetryOptions, telemetryDataCollectors);

            var result = telemetryService.TryGetTelemetryReportData(out var telemetry);

            Assert.IsTrue(result);

            Assert.Contains(TelemetryData.TelemetryId, telemetry.Keys);
            Assert.AreEqual(Guid.Parse("0f1785c5-7ba0-4c52-ab62-863bd2c8f3fe"), telemetry[TelemetryData.TelemetryId]);

            Assert.Contains(TelemetryData.Network, telemetry.Keys);
            Assert.AreEqual(true, telemetry[TelemetryData.Network]);
        }

        [Test]
        public void CanCollectUmbracoVersionTelemetryData()
        {
            var telemetryOptions = CreateTelemetrySettings(TelemetryLevel.Basic);
            var umbracoVersion = Mock.Of<IUmbracoVersion>(x => x.SemanticVersion == new SemVersion(9, 1, 1, "-rc", "-ad2f4k2d"));
            var telemetryDataCollectors = new[]
            {
                new UmbracoVersionTelemetryDataCollector(umbracoVersion)
            };

            var telemetryService = new TelemetryService(telemetryOptions, telemetryDataCollectors);

            var result = telemetryService.TryGetTelemetryReportData(out var telemetry);

            Assert.IsTrue(result);

            Assert.Contains(TelemetryData.UmbracoVersion, telemetry.Keys);
            Assert.AreEqual("9.1.1-rc", telemetry[TelemetryData.UmbracoVersion]);
        }

        [Test]
        public void CanCollectPackageVersionsTelemetryData()
        {
            var telemetryOptions = CreateTelemetrySettings(TelemetryLevel.Basic);

            var versionPackageName = "VersionPackage";
            var packageVersion = "1.0.0";
            var noVersionPackageName = "NoVersionPackage";
            var doNotTrackPackageName = "DoNotTrack";
            var trackingAllowedPackageName = "TrackingAllowed";
            var manifestParserMock = new Mock<IManifestParser>();
            manifestParserMock.Setup(x => x.GetManifests()).Returns(new[]
            {
                new PackageManifest() { PackageName = versionPackageName, Version = packageVersion },
                new PackageManifest() { PackageName = noVersionPackageName },
                new PackageManifest() { PackageName = doNotTrackPackageName, AllowPackageTelemetry = false },
                new PackageManifest() { PackageName = trackingAllowedPackageName, AllowPackageTelemetry = true }
            });
            var telemetryDataCollectors = new[]
            {
                new PackageVersionsTelemetryDataCollector(manifestParserMock.Object)
            };

            var telemetryService = new TelemetryService(telemetryOptions, telemetryDataCollectors);

            var result = telemetryService.TryGetTelemetryReportData(out var telemetry);

            Assert.IsTrue(result);

            Assert.Contains(TelemetryData.PackageVersions, telemetry.Keys);

            Assert.Multiple(() =>
            {
                var packages = telemetry[TelemetryData.PackageVersions] as IEnumerable<PackageTelemetry>;

                Assert.NotNull(packages);
                Assert.AreEqual(3, packages.Count());

                var versionPackage = packages.FirstOrDefault(x => x.Name == versionPackageName);
                Assert.IsNotNull(versionPackage);
                Assert.AreEqual(packageVersion, versionPackage.Version);

                var noVersionPackage = packages.FirstOrDefault(x => x.Name == noVersionPackageName);
                Assert.IsNotNull(noVersionPackage);
                Assert.AreEqual(null, noVersionPackage.Version);

                var trackingAllowedPackage = packages.FirstOrDefault(x => x.Name == trackingAllowedPackageName);
                Assert.IsNotNull(trackingAllowedPackage);
            });
        }

        private IOptionsMonitor<TelemetrySettings> CreateTelemetrySettings(TelemetryLevel level)
        {
            var telemetrySettings = new TelemetrySettings();
            telemetrySettings.Set(level);

            return Mock.Of<IOptionsMonitor<TelemetrySettings>>(x => x.CurrentValue == telemetrySettings);
        }
    }
}