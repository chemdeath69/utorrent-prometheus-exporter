using CommandLine;
using Prometheus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UTorrent.Api;
using UTorrent.Api.Data;

namespace UTorrentExporter
{
    /// <summary>
    /// Options read in using the Command Line
    /// </summary>
    public class CLOptions
    {
        [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.", Default = false)]
        public bool Verbose { get; set; }

        [Option('i', "utorrent-ip-address", Required = false, HelpText ="Set utorrent WebAPI address.", Default = "127.0.0.1")]
        public string UTorrentIpAddress { get; set; }

        [Option('p', "utorrent-port", Required = false, HelpText ="Set utorrent WebAPI port.", Default = 8080)]
        public ushort UTorrentPort { get; set; }

        [Option('a', "listen-address", Required = false, HelpText = "Listen Address for prometheus scraper.", Default = "127.0.0.1")]
        public string PrometheusAddress { get; set; }

        [Option('l', "listen-port", Required = false, HelpText = "Set listen port for prometheus scraper.", Default = 8091)]
        public ushort PrometheusPort { get; set; }

        [Option('u', "utorrent-username", Required = true, HelpText = "Username to connect to utorrent webapi.")]
        public string UTorrentUsername { get; set; }

        [Option('w', "utorrent-password", Required = true, HelpText = "Password to conenct to utorrent webapi.")]
        public string UTorrentPassword { get; set; }

        [Option("poll-speed-seconds", Required = false, HelpText = "The number of seconds between utorrent poll", Default = 10)]
        public long UTorrentPollSeconds { get; set; }
    }

    class Program
    {
        #region Metrics    
        /// <summary>
        /// Definitions of all metrics used in this exporter.
        /// </summary>
        private static readonly string MetricBytesDownloadedCounterName = "utorrent_total_bytes_downloaded";
        private static readonly Counter MetricBytesDownloaded =
            Metrics.CreateCounter(MetricBytesDownloadedCounterName
                , "The total number of bytes downloaded using the UTorrent instance"
                , new CounterConfiguration
                {
                    LabelNames = new [] {"torrent", "hash"}
                });

        private static readonly string MetricBytesUploadedCounterName = "utorrent_total_bytes_uploaded";
        private static readonly Counter MetricBytesUploaded =
            Metrics.CreateCounter(MetricBytesUploadedCounterName
                , "The total number of bytes uploaded using the UTorrent instance"
                , new CounterConfiguration
                {
                    LabelNames = new[] { "torrent", "hash" }
                });

        private static readonly string MetricDownloadSpeedGaugeName = "utorrent_current_download_speed";
        private static readonly Gauge MetricDownloadSpeed =
            Metrics.CreateGauge(MetricDownloadSpeedGaugeName
                , "The current download speed using the UTorrent instance"
                , new GaugeConfiguration
                {
                    LabelNames = new[] { "torrent", "hash" }
                });

        private static readonly string MetricUploadSpeedGaugeName = "utorrent_current_upload_speed";
        private static readonly Gauge MetricUploadSpeed =
            Metrics.CreateGauge(MetricUploadSpeedGaugeName
                , "The current upload speed using the UTorrent instance"
                , new GaugeConfiguration
                {
                    LabelNames = new[] { "torrent", "hash" }
                });

        private static readonly string MetricTotalSizeGaugeName = "utorrent_torrent_total_size";
        private static readonly Gauge MetricTotalSize =
            Metrics.CreateGauge(MetricTotalSizeGaugeName
                , "The current total torrent size using the UTorrent instance"
                , new GaugeConfiguration
                {
                    LabelNames = new[] { "torrent", "hash" }
                });

        private static readonly string MetricRemainingSizeGaugeName = "utorrent_torrent_remaining_size";
        private static readonly Gauge MetricRemainingSize =
            Metrics.CreateGauge(MetricRemainingSizeGaugeName
                , "The current remaining torrent size using the UTorrent instance"
                , new GaugeConfiguration
                {
                    LabelNames = new[] { "torrent", "hash" }
                });

        private static readonly string MetricAvailableGaugeName = "utorrent_torrent_available_amount";
        private static readonly Gauge MetricAvailable =
            Metrics.CreateGauge(MetricAvailableGaugeName
                , "The availability of a torrent with less than 1 being unfulfilled"
                , new GaugeConfiguration
                {
                    LabelNames = new[] { "torrent", "hash" }
                });

        private static readonly string MetricPeersConnectedGaugeName = "utorrent_torrent_peers_connected";
        private static readonly Gauge MetricPeersConnected =
            Metrics.CreateGauge(MetricPeersConnectedGaugeName
                , "The number of peers connected for a specific torrent"
                , new GaugeConfiguration
                {
                    LabelNames = new[] { "torrent", "hash" }
                });

        private static readonly string MetricPeersInSwarmGaugeName = "utorrent_torrent_peers_in_swarm";
        private static readonly Gauge MetricPeersInSwarm =
            Metrics.CreateGauge(MetricPeersInSwarmGaugeName
                , "The number of peers in swarm for a specific torrent"
                , new GaugeConfiguration
                {
                    LabelNames = new[] { "torrent", "hash" }
                });

        private static readonly string MetricSeedsConnectedGaugeName = "utorrent_torrent_seeds_connected";
        private static readonly Gauge MetricSeedsConnected =
            Metrics.CreateGauge(MetricSeedsConnectedGaugeName
                , "The number of seeds connected for a specific torrent"
                , new GaugeConfiguration
                {
                    LabelNames = new[] { "torrent", "hash" }
                });

        private static readonly string MetricSeedsInSwarmGaugeName = "utorrent_torrent_seeds_in_swarm";
        private static readonly Gauge MetricSeedsInSwarm =
            Metrics.CreateGauge(MetricSeedsInSwarmGaugeName
                , "The number of seeds in swarm for a specific torrent"
                , new GaugeConfiguration
                {
                    LabelNames = new[] { "torrent", "hash" }
                });

        private static readonly string MetricRatioGaugeName = "utorrent_torrent_ratio";
        private static readonly Gauge MetricRatio =
            Metrics.CreateGauge(MetricRatioGaugeName
                , "The current ratio for a specific torrent"
                , new GaugeConfiguration
                {
                    LabelNames = new[] { "torrent", "hash" }
                });

        private static readonly string MetricProgressGaugeName = "utorrent_torrent_progress";
        private static readonly Gauge MetricProgress =
            Metrics.CreateGauge(MetricProgressGaugeName
                , "The current progress for a specific torrent"
                , new GaugeConfiguration
                {
                    LabelNames = new[] { "torrent", "hash" }
                });
        #endregion

        static void Main(string[] args)
        {
            // Note:  This is a really simple implementation, but it works.
            Dictionary<string, long> LastBytesDownloadedState = new Dictionary<string, long>();
            Dictionary<string, long> LastBytesUploadedState = new Dictionary<string, long>();

            CLOptions options = null;
            Parser.Default.ParseArguments<CLOptions>(args)
                .WithParsed<CLOptions>(o =>
                {
                    options = o;
                });

            Console.WriteLine($"Starting Prometheus scrape listener on {options.PrometheusAddress}:{options.PrometheusPort}");
            var promServer = new MetricServer(options.PrometheusAddress, options.PrometheusPort);
            promServer.Start();

            Console.WriteLine($"Creating UTorrent WebAPI client on {options.UTorrentIpAddress}:{options.UTorrentPort}");
            UTorrentClient client = new UTorrentClient(options.UTorrentIpAddress, options.UTorrentPort, 
                options.UTorrentUsername, options.UTorrentPassword);
            for (; ; )
            {
                try
                {
                    if (options.Verbose) Console.WriteLine($"Processing UTorrent Poll at {DateTime.Now}");
                    var response = client.GetList();
                    if (response != null && response.Error == null && response.Result.Torrents != null)
                    {
                        // summarize all sub-torrent data
                        foreach (var torrent in response.Result.Torrents)
                        {
                            // MetricBytesDownloaded
                            if (LastBytesDownloadedState.ContainsKey(torrent.Hash))
                            {
                                var increaseAmt = torrent.Downloaded - LastBytesDownloadedState[torrent.Hash];
                                MetricBytesDownloaded.WithLabels(SanitizeString(torrent.Name), torrent.Hash).Inc(increaseAmt);
                                if (options.Verbose) Console.WriteLine($"{MetricBytesDownloadedCounterName} with torrent = \"{SanitizeString(torrent.Name)}\" increased by {increaseAmt}.");
                            }

                            LastBytesDownloadedState[torrent.Hash] = torrent.Downloaded;

                            // MetricBytesUploaded
                            if (LastBytesUploadedState.ContainsKey(torrent.Hash))
                            {
                                var increaseAmt = torrent.Uploaded - LastBytesUploadedState[torrent.Hash];
                                MetricBytesUploaded.WithLabels(SanitizeString(torrent.Name), torrent.Hash).Inc(increaseAmt);
                                if (options.Verbose) Console.WriteLine($"{MetricBytesUploadedCounterName} with torrent = \"{SanitizeString(torrent.Name)}\" increased by {increaseAmt}.");
                            }

                            LastBytesUploadedState[torrent.Hash] = torrent.Uploaded;

                            MetricDownloadSpeed.WithLabels(SanitizeString(torrent.Name), torrent.Hash).Set(torrent.DownloadSpeed);
                            if (options.Verbose) Console.WriteLine($"{MetricDownloadSpeedGaugeName} with torrent = \"{SanitizeString(torrent.Name)}\" set to {torrent.DownloadSpeed}.");
                            MetricUploadSpeed.WithLabels(SanitizeString(torrent.Name), torrent.Hash).Set(torrent.UploadSpeed);
                            if (options.Verbose) Console.WriteLine($"{MetricUploadSpeedGaugeName} with torrent = \"{SanitizeString(torrent.Name)}\" set to {torrent.UploadSpeed}.");

                            MetricTotalSize.WithLabels(SanitizeString(torrent.Name), torrent.Hash).Set(torrent.Size);
                            if (options.Verbose) Console.WriteLine($"{MetricTotalSizeGaugeName} with torrent = \"{SanitizeString(torrent.Name)}\" set to {torrent.Size}.");
                            MetricRemainingSize.WithLabels(SanitizeString(torrent.Name), torrent.Hash).Set(torrent.Remaining);
                            if (options.Verbose) Console.WriteLine($"{MetricRemainingSizeGaugeName} with torrent = \"{SanitizeString(torrent.Name)}\" set to {torrent.Remaining}.");
                            MetricAvailable.WithLabels(SanitizeString(torrent.Name), torrent.Hash).Set((double)torrent.Availability / (double)65536);
                            if (options.Verbose) Console.WriteLine($"{MetricAvailableGaugeName} with torrent = \"{SanitizeString(torrent.Name)}\" set to {(double)torrent.Availability / (double)65536}.");
                            MetricPeersConnected.WithLabels(SanitizeString(torrent.Name), torrent.Hash).Set(torrent.PeersConnected);
                            if (options.Verbose) Console.WriteLine($"{MetricPeersConnectedGaugeName} with torrent = \"{SanitizeString(torrent.Name)}\" set to {torrent.PeersConnected}.");
                            MetricPeersInSwarm.WithLabels(SanitizeString(torrent.Name), torrent.Hash).Set(torrent.PeersInSwarm);
                            if (options.Verbose) Console.WriteLine($"{MetricPeersInSwarmGaugeName} with torrent = \"{SanitizeString(torrent.Name)}\" set to {torrent.PeersInSwarm}.");
                            MetricSeedsConnected.WithLabels(SanitizeString(torrent.Name), torrent.Hash).Set(torrent.SeedsConnected);
                            if (options.Verbose) Console.WriteLine($"{MetricSeedsConnectedGaugeName} with torrent = \"{SanitizeString(torrent.Name)}\" set to {torrent.SeedsConnected}.");
                            MetricSeedsInSwarm.WithLabels(SanitizeString(torrent.Name), torrent.Hash).Set(torrent.SeedsInSwarm);
                            if (options.Verbose) Console.WriteLine($"{MetricSeedsInSwarmGaugeName} with torrent = \"{SanitizeString(torrent.Name)}\" set to {torrent.SeedsInSwarm}.");
                            MetricRatio.WithLabels(SanitizeString(torrent.Name), torrent.Hash).Set(torrent.Ratio);
                            if (options.Verbose) Console.WriteLine($"{MetricRatioGaugeName} with torrent = \"{SanitizeString(torrent.Name)}\" set to {torrent.Ratio}.");
                            MetricProgress.WithLabels(SanitizeString(torrent.Name), torrent.Hash).Set((double)torrent.Progress / 10.0);
                            if (options.Verbose) Console.WriteLine($"{MetricProgressGaugeName} with torrent = \"{SanitizeString(torrent.Name)}\" set to {(double)torrent.Progress / 10.0}.");
                        }
                    }
                    else if (response.Error != null)
                    {
                        Console.WriteLine($"Error Encountered: {response.Error.Message}");
                    }
                }
                catch(InvalidCredentialException ex)
                {
                    Console.WriteLine($"Failed to connect to uTorrent WebAPI: Invalid Login Credentials");
                }
                catch(Exception ex)
                {
                    Console.WriteLine($"Fatal Error: {ex.Message}");
                }

                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(options.UTorrentPollSeconds));
            }
        }

        static string SanitizeString(string input)
        {
            return input.Replace("\"", "").Replace("\\", "");
        }
    }
}
