using System.CommandLine;
using Microsoft.Extensions.Logging;

var rootCommand = new RootCommand();

var allowedDomainsOption = new Option<string[]>
(
    name: "--allowed-domains",
    description: "Set the domains that crawler allowed to crawl"
)
{
    IsRequired = true,
    AllowMultipleArgumentsPerToken = true
};

var startUrisOption = new Option<Uri[]>
(
    name: "--start-uris",
    description: "Set the uris form which the crawl will start crawling"
)
{
    IsRequired = true,
    AllowMultipleArgumentsPerToken = true
};

var saveToOption = new Option<string>
(
    name: "--save-to",
    description: "Set the directory in which the content will be saved"
)
{
    IsRequired = true
};


var logLevelOption = new Option<LogLevel>
(
    name: "--log-level",
    description: "Set the log level",
    getDefaultValue: () => LogLevel.Warning
);

var maxWorkesOption = new Option<int>
(
     name: "--max-workers",
     description: "Set the maximum amount of running crawler processors",
     getDefaultValue: () => Environment.ProcessorCount
);

var reqestTimeoutOption = new Option<int>
(
    name: "--request-timeout",
    description: "Set the maximum request timeout in seconds",
    getDefaultValue: () => 10
);


rootCommand.Add(allowedDomainsOption);
rootCommand.Add(startUrisOption);
rootCommand.Add(saveToOption);
rootCommand.Add(logLevelOption);
rootCommand.Add(maxWorkesOption);
rootCommand.Add(reqestTimeoutOption);

rootCommand.SetHandler
(
    (allowedDomains, startUris, saveTo, minLogLevel, maxWorkers, requestTimeout) =>
    {
        var crawler = new CrawlerBuilder()
            .AllowedDomains(allowedDomains)
            .StartUris(startUris)
            .SaveTo(saveTo)
            .MinLogLevel(minLogLevel)
            .MaxWorkers(maxWorkers)
            .RequestTimeout(requestTimeout)
            .Build();
        crawler.Crawl();
    },
    allowedDomainsOption,
    startUrisOption,
    saveToOption,
    logLevelOption,
    maxWorkesOption,
    reqestTimeoutOption
);

rootCommand.Invoke(args);
