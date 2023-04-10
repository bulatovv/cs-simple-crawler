using System.Net;
using System.Net.Mime;
using Microsoft.Extensions.Logging;
using HtmlAgilityPack;


class Response
{
    public Uri Location { get; private set; }
    public byte[] Data { get; private set; }
    public ContentType ContentType { get; private set; }

    public Response(HttpResponseMessage response)
    {
        Location = response.RequestMessage?.RequestUri!;
            
        ContentType = new ContentType(response.Content.Headers.ContentType?.MediaType ?? ""); 
    
        using (MemoryStream ms = new MemoryStream())
        {
            response.Content.ReadAsStream().CopyTo(ms);
            Data = ms.ToArray();
        }
    }

    public void Save(string location)
    {
        File.WriteAllBytes(location, Data);
    }
}

enum PageLabel
{
    Visited
}


class CrawlerBuilder
{
    private LogLevel _logLevel = LogLevel.None;
    private List<string> _allowedDomains = new List<string>();
    private List<Uri> _startUris = new List<Uri>();
    private static string? _saveTo = null;
    private int _maxWorkers = Environment.ProcessorCount;
    private TimeSpan? _timeout = null;

    public CrawlerBuilder MaxWorkers(int workers)
    {
        _maxWorkers = workers;
        return this;
    }

    public CrawlerBuilder MinLogLevel(LogLevel logLevel)
    {
        _logLevel = logLevel;
        return this;
    }

    public CrawlerBuilder AllowedDomains(params string[] domains)
    {
        _allowedDomains.AddRange(domains);
        return this;
    }

    public CrawlerBuilder StartUris(params Uri[] uris)
    {
        _startUris.AddRange
        (
            uris
                .Where(uri => uri.IsAbsoluteUri)
        );
        
        if (_startUris.Count != uris.Length)
            throw new ArgumentException(String.Join(",", uris.Where(uri => !uri.IsAbsoluteUri)));

        return this;
    }

    public CrawlerBuilder RequestTimeout(int seconds)
    {
        _timeout = TimeSpan.FromSeconds(seconds);
        return this;
    }

    public CrawlerBuilder SaveTo(string path)
    {
        _saveTo = path;
        return this;
    }

    public Crawler Build()
    {
        var logger = LoggerFactory
            .Create
            (
                loggingBuilder => 
                    loggingBuilder.AddSimpleConsole
                    (
                        options =>
                        {
                            options.SingleLine = true;
                        }
                    )
                    .SetMinimumLevel(_logLevel)
            )
            .CreateLogger("Crawler");
 
        var httpClient = new HttpClient
        (
            new HttpClientHandler() { AllowAutoRedirect = false }, 
            false
        );

        if (_timeout != null)
            httpClient.Timeout = (TimeSpan) _timeout;

        var crawlHist = new Graph<string, PageLabel>();
        
        var workersSem = new SemaphoreSlim(_maxWorkers);

        return new Crawler
        (
            allowedDomains: _allowedDomains,
            startUris: _startUris,
            saveTo: _saveTo,
            logger: logger,
            httpClient: httpClient,
            crawlHist: crawlHist,
            workersSem: workersSem
        );
    }

}

class Crawler
{
    private static List<string> _allowedSchemes = new List<string> 
    {
        "http", "https"
    };
    
    private List<string> _allowedDomains;
    private List<Uri> _startUris;
    private static string? _saveTo;

    private IGraph<string, PageLabel> _crawlHist;
    private HttpClient _httpClient;
    private ILogger _logger;
    private SemaphoreSlim _workersSem;
    
    public Crawler
    (
        List<string> allowedDomains,
        List<Uri> startUris,
        string? saveTo,
        
        ILogger logger,
        HttpClient httpClient,
     
        IGraph<string, PageLabel> crawlHist,
        SemaphoreSlim workersSem
    )
    {
        _allowedDomains = allowedDomains;
        _startUris = startUris;
        _saveTo = saveTo;


        _logger = logger;
        _httpClient = httpClient;
        
        _crawlHist = crawlHist;
        
        _workersSem = workersSem; 
    }
    

    public void SaveCrawlHist()
    {
        if (_saveTo == null)
            return;


        using (var output = new StreamWriter(Path.Combine(_saveTo, "sitemap.dot")))
        {
            output.WriteLine("digraph {");

            _crawlHist.Nodes
                .Where(node => node.Neighbors.Any())
                .ToList()
                .ForEach
                (
                    node => output.WriteLine
                    (
                        String.Format
                        (
                            "\t\"{0}\" -> {{{1}}}",
                            node.Key,
                            String.Join(' ', node.Neighbors.Select(e => "\"" + e.Key + "\""))
                        )
                    )
                );
            output.Write("}");
        }
    }

    public void Crawl()
    {
        foreach (var uri in _startUris) 
            if (CanCrawlUri(uri))
                CrawlRoutine(uri);
        SaveCrawlHist();
        
        if (_saveTo != null)
        {
            var savedDirs = Directory
                .EnumerateDirectories(_saveTo)
                .Where(dir => _allowedDomains.Contains(new DirectoryInfo(dir).Name));

            foreach (var dir in savedDirs)
                NormalizeDirs(dir);
        }

    }

    private void CrawlRoutine(Uri uri, Uri? fromUri = null)
    {
        Task.Factory
            .StartNew
            (
                () => 
                {
                    if(! _crawlHist.AddNode(CrawlHistKey(uri), PageLabel.Visited))
                        return;
                    
                    _workersSem.Wait();
                    var response = GetPage(uri);
                    _workersSem.Release();

                    if (response == null)
                        return;

                    var content = new Response(response);
                    SavePageContent(content);

                    
                    if (fromUri != null)
                        _crawlHist.AddEdge
                        (
                            CrawlHistKey(fromUri),
                            CrawlHistKey(uri)
                        );

                    foreach (var siblingUri in ExtractHrefs(content)) 
                    {
                        if (CanCrawlUri(siblingUri)) 
                        {
                            var found = _crawlHist.Find(CrawlHistKey(siblingUri));
                            if (found != null && found.Label == PageLabel.Visited) 
                            { 
                                continue;
                            }

                            Task.Factory.StartNew
                            (
                                () => CrawlRoutine(siblingUri, uri),
                                TaskCreationOptions.AttachedToParent
                            );
                        }    
                    }
                }
            )
            .Wait();
    }

    private void SavePageContent(Response content)
    {
        if (_saveTo == null) 
            return;

        var location = content.Location.AbsolutePath;
        
        if (location.StartsWith("/"))
            location = location.Substring(1);

        if (location.EndsWith("/"))
            location = location.Substring(0, location.LastIndexOf("/"));

        var fullLocation = Path.Combine(_saveTo, content.Location.Host, location);   
        if (content.ContentType.MediaType.Equals("text/html")) 
        {
            fullLocation = Path.Combine(fullLocation, "index.html");
        }

        var parentDir = Directory.GetParent(fullLocation)!.FullName;

        if (! Directory.Exists(parentDir))
            Directory.CreateDirectory(parentDir);
        content.Save(fullLocation);
    }

    private void NormalizeDirs(string path)
    {
        var dirs = Directory.EnumerateDirectories(path);
        var files = Directory.EnumerateFiles(path);
    

        if 
        (
            dirs.Count() == 0 && files.Count() == 1 && 
            files.Contains(Path.Combine(path, "index.html"))
        )
        {
            Directory.Move(path, path + ".temp");
            File.Move(Path.Combine(path + ".temp", "index.html"), path);
            Directory.Delete(path + ".temp");
            return;
        }

        foreach (var dir in dirs)
        {
            NormalizeDirs(dir);
        }
    }

    private string CrawlHistKey(Uri uri)
    {
        return uri.Host + uri.AbsolutePath; 
    }

    private HttpResponseMessage? GetPage(Uri uri)
    {

        using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
        {
            HttpResponseMessage response;

            try
            { 
                response = _httpClient.Send(request);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException) 
            {
                _logger.Log
                (
                    LogLevel.Error, 
                    String.Format
                    (
                        "{0}: {1}",
                        uri.OriginalString,
                        "Timeout"
                    )
                );    
                return null;
            }

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    _logger.Log
                    (
                        LogLevel.Information, 
                        String.Format
                        (
                            "{0}: {1}",
                            response.RequestMessage?.RequestUri?.OriginalString,
                            response.StatusCode
                        )
                    ); 
                    return response;

                case HttpStatusCode.MovedPermanently or HttpStatusCode.Found or
                     HttpStatusCode.TemporaryRedirect or HttpStatusCode.MovedPermanently: 
                    Uri redirectUri = response.Headers.Location!;
                    if (!redirectUri.IsAbsoluteUri)
                    {
                        redirectUri = new Uri(request.RequestUri!.GetLeftPart(UriPartial.Authority) + redirectUri);
                    }
                    if (CanCrawlUri(redirectUri))
                        return GetPage(redirectUri);
                    else
                        return null;
                default:
                    _logger.Log
                    (
                        LogLevel.Warning, 
                        String.Format
                        (
                            "{0}: {1}",
                            response.RequestMessage?.RequestUri?.OriginalString,
                            response.StatusCode
                        )
                    );
                    return null;
            }
        }
    }

    private List<Uri> ExtractHrefs(Response response)
    { 
        var collectedLinks = new List<Uri>();

        if (response.ContentType.MediaType != "text/html")
            return collectedLinks;

        var htmlDoc = new HtmlDocument();

        using (MemoryStream ms = new MemoryStream(response.Data))
        {
            htmlDoc.Load(ms);
        }
        
        var root = htmlDoc.DocumentNode;

        collectedLinks.AddRange
        (
            root.SelectNodes("//a[@href]")
                .Cast<HtmlNode>()
                .Select(node => new Uri(response.Location, node.Attributes["href"].Value))
        );

        collectedLinks.AddRange
        (
            root.SelectNodes("//img[@src]")
                .Cast<HtmlNode>()
                .Select(node => new Uri(response.Location, node.Attributes["src"].Value))
        );

        collectedLinks.AddRange
        (
            root.SelectNodes("//link[@href]")
                .Cast<HtmlNode>()
                .Select(node => new Uri(response.Location, node.Attributes["href"].Value))
        );


        return collectedLinks;
    }

    private bool CanCrawlUri(Uri uri)
    {
        return IsAllowedScheme(uri) && IsAllowedDomain(uri);
    }


    private bool IsAllowedDomain(Uri uri)
    {
        if (_allowedDomains.Contains(uri.Host))
        {
            return true;
        }
        else
        {
            _logger.Log
            (
                LogLevel.Information,
                String.Format
                (
                    "{0}: {1}",
                    uri.OriginalString,
                    "Crawling is not allowed for this domain"
                )
            );
            return false;
        }
    }

    private bool IsAllowedScheme(Uri uri)
    {
        return _allowedSchemes.Contains(uri.Scheme);
    } 
}
