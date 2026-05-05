using OpenGraphNet;
using OpenGraphNet.Metadata;
using OpenGraphNet.Namespaces;
using System.Xml;
using CommandLine;

internal partial class Program
{
	private static async Task Main(string[] args)
	{
		var result = Parser.Default.ParseArguments<Options>(args);

		if (result.Tag == ParserResultType.NotParsed)
		{
			foreach (var error in result.Errors)
			{
				Console.WriteLine(error);
			}

			return;
		}

		var options = result.Value;

		options.OutputOptions();

		// The registry starts with the OG elements already marked as required. Since we make checking for them optional,
		// we need to clear that namespace out in case the user has opted to ignore it. By default it gets added back in.
		NamespaceRegistry.Instance.RemoveNamespace("og");

		EnsureNamespacesRegistered(options.OptionalNamespaces);
		EnsureRequiredElements(options.RequiredElements);

		var locations = await GetLocationsFromSitemap(options.Sitemap);

		if (options.Verbose)
		{
			Console.WriteLine($"Retrieved {locations.Count} URLs from sitemap {options.Sitemap}");
		}

		var issueList = new List<string>();

		for (var i = 0; i < locations.Count; i++)
		{
			var url = locations[i];
			var targetUrl = string.IsNullOrEmpty(options.LocalBaseUrl)
				? url
				: UseLocalBaseUrl(url, options.LocalBaseUrl);

			if (options.Progress)
			{
				Console.WriteLine($"{i+1}/{locations.Count} Checking {targetUrl}");
			}

			var issues = await FindIssues(targetUrl, result.Value);
			issueList.AddRange(issues);
		}

		OutputIssues(issueList);
	}

	static void EnsureNamespacesRegistered(IEnumerable<string> namespaces)
	{
		var registered = NamespaceRegistry.Instance.Namespaces;

		foreach (var ns in namespaces)
		{
			if (registered.ContainsKey(ns)) { continue; }

			NamespaceRegistry.Instance.AddNamespace(
					prefix: ns,
					schemaUri: "");
		}
	}

	static void EnsureRequiredElements(IEnumerable<KeyValuePair<string, string>> elements) {

		var registered = NamespaceRegistry.Instance.Namespaces;

		foreach (var element in elements)
		{
			var ns = element.Key;

			if (!registered.ContainsKey(ns)) {
				NamespaceRegistry.Instance.AddNamespace(
					prefix: ns,
					schemaUri: "");
			}

			registered[ns].RequiredElements.Add(element.Value);
		}
	}

	static string UseLocalBaseUrl(string url, string localBaseUrl) 
	{
		var original = new Uri(url);

		return url.Replace(original.GetLeftPart(UriPartial.Authority), localBaseUrl);
	}

	static async Task<List<String>> FindIssues(string url, Options options)
	{
		var issueList = new List<string>();

		// Not validating the spec because it will immediately throw an error if something is missing; we want to aggregate
		// _all_ of the errors in a report
		OpenGraph graph = await OpenGraph.ParseUrlAsync(url, validateSpecification: false);

		foreach (var @namespace in options.RequiredNamespaces)
		{
			if (!graph.Namespaces.TryGetValue(@namespace, out OpenGraphNamespace? graphNamespace))
			{
				issueList.Add($"{url} has no metadata for {@namespace}");
				continue;
			}

			issueList = [.. issueList, .. ValidateRequiredElements(url, graph, graphNamespace)];
		}

		foreach (var @namespace in options.OptionalNamespaces)
		{
			if (!graph.Namespaces.TryGetValue(@namespace, out OpenGraphNamespace? graphNamespace))
			{
				continue;
			}

			issueList = [.. issueList, .. ValidateRequiredElements(url, graph, graphNamespace)];
		}

		var unexpected = graph.Namespaces
			.Where(ns => !options.RequiredNamespaces.Contains(ns.Key) && !options.OptionalNamespaces.Contains(ns.Key));

		foreach (var @namespace in unexpected)
		{
			// This will find unexpected normal OG stuff (like a game: tag when you don't ask about them),
			// but won't see misspelled or fat-fingered tags (like if you mean "profile:" and type "porfile:").
			issueList.Add($"Found unexpected namespace {@namespace} in {url}");
		}

		return issueList;
	}

	static void OutputIssues(List<String> issues)
	{
		if (issues.Count == 0)
		{
			Console.WriteLine("No issues found.");
			return;
		}

		Console.WriteLine("Issues found:");

		foreach (var issue in issues)
		{
			Console.WriteLine(issue);
		}
	}

	static async Task<List<string>> GetLocationsFromSitemap(string sitemapUrl)
	{
		using var client = new HttpClient();

		client.DefaultRequestHeaders.UserAgent.TryParseAdd("OpenGraphChecker/0.1.0");

		var sitemap = await client.GetAsync(sitemapUrl);
		var siteMapXml = await sitemap.Content.ReadAsStringAsync();

		var siteMapDoc = new XmlDocument();
		siteMapDoc.LoadXml(siteMapXml);

		var locations = siteMapDoc.GetElementsByTagName("loc", "http://www.sitemaps.org/schemas/sitemap/0.9").Cast<XmlNode>();
		return [.. locations.Select(x => x.InnerText)];
	}

	static IEnumerable<string> ValidateRequiredElements(string url, OpenGraph graph, OpenGraphNamespace @namespace)
	{
		var prefix = @namespace.Prefix;
		var required = NamespaceRegistry.Instance.Namespaces[prefix].RequiredElements;

		foreach (var requiredElement in required)
		{
			var element = requiredElement;
			string? targetProperty = null;

			if (element.Contains(':'))
			{
				var tokens = requiredElement.Split(':');
				element = tokens[0] ?? requiredElement;
				targetProperty = tokens[1];
			}

			var key = $"{prefix}:{element}";

			if (targetProperty is null)
			{
				if (string.IsNullOrEmpty(graph.Metadata[key].Value()))
				{
					yield return $"{url} is missing required value for key {key}";
					continue;
				}
			}
			else
			{
				var metadata = graph.Metadata[key];
				if (metadata.Count == 0)
				{
					yield return $"{url} is missing required property {targetProperty} for key {key}";
					continue;
				}

				var metadataProperties = metadata[0].Properties;
				if (metadataProperties.Count == 0)
				{
					yield return $"{url} is missing required property {targetProperty} for key {key}";
					continue;
				}
				else
				{
					if (!metadataProperties.ContainsKey(targetProperty))
					{
						yield return $"{url} is missing required property {targetProperty} for key {key}";
						continue;
					}
				}
			}
		}
	}
}