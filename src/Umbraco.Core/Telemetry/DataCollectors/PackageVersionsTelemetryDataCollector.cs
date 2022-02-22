using System;
using System.Collections.Generic;
using Umbraco.Cms.Core.Manifest;
using Umbraco.Cms.Core.Telemetry.Models;

namespace Umbraco.Cms.Core.Telemetry.DataCollectors
{
    /// <summary>
    /// Collects package versions telemetry data.
    /// </summary>
    /// <seealso cref="Umbraco.Cms.Core.Telemetry.ITelemetryDataCollector" />
    internal class PackageVersionsTelemetryDataCollector : ITelemetryDataCollector
    {
        private readonly IManifestParser _manifestParser;

        private static readonly IEnumerable<TelemetryData> s_data = new[]
        {
            TelemetryData.PackageVersions
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="PackageVersionsTelemetryDataCollector" /> class.
        /// </summary>
        public PackageVersionsTelemetryDataCollector(IManifestParser manifestParser) => _manifestParser = manifestParser;

        /// <inheritdoc/>
        public IEnumerable<TelemetryData> Data => s_data;

        /// <inheritdoc/>
        public object Collect(TelemetryData telemetryData) => telemetryData switch
        {
            TelemetryData.PackageVersions => GetPackageVersions(),
            _ => throw new NotSupportedException()
        };

        private IEnumerable<PackageTelemetry> GetPackageVersions()
        {
            List<PackageTelemetry> packages = new();

            foreach (PackageManifest manifest in _manifestParser.GetManifests())
            {
                if (string.IsNullOrEmpty(manifest.PackageName) || manifest.AllowPackageTelemetry is false)
                {
                    continue;
                }

                packages.Add(new PackageTelemetry
                {
                    Name = manifest.PackageName,
                    Version = !string.IsNullOrEmpty(manifest.Version) ? manifest.Version : null
                });
            }

            return packages;
        }
    }
}