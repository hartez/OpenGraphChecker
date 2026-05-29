using CommandLine;
using OpenGraphNet;
using OpenGraphNet.Metadata;
using OpenGraphNet.Namespaces;
using System.Xml;

internal partial class Program
{
	static readonly HttpClient _httpClient = new();
	static Dictionary<Uri, bool> _knownImageStatus = [];

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
			var url = new Uri(locations[i]);

			var target = AdjustTarget(url, options);

			if (options.Progress)
			{
				Console.WriteLine($"{i + 1}/{locations.Count} Checking {target}");
			}

			var issues = await FindIssues(target, result.Value);
			issueList.AddRange(issues);
		}

		OutputIssues(issueList);
	}

	static Uri AdjustTarget(Uri target, Options options)
	{
		return options.UseLocalBase
					? UseLocalBase(target, new Uri(options.LocalBaseUrl))
					: target;
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

	static void EnsureRequiredElements(IEnumerable<KeyValuePair<string, string>> elements)
	{

		var registered = NamespaceRegistry.Instance.Namespaces;

		foreach (var element in elements)
		{
			var ns = element.Key;

			if (!registered.ContainsKey(ns))
			{
				NamespaceRegistry.Instance.AddNamespace(
					prefix: ns,
					schemaUri: "");
			}

			registered[ns].RequiredElements.Add(element.Value);
		}
	}

	static Uri UseLocalBase(Uri uri, Uri localBaseUri)
	{
		var original = uri;

		return new Uri(uri.ToString().Replace(original.GetLeftPart(UriPartial.Authority), localBaseUri.ToString()));
	}

	static async Task<List<String>> FindIssues(Uri uri, Options options)
	{
		var issueList = new List<string>();

		// Not validating the spec because it will immediately throw an error if something is missing; we want to aggregate
		// _all_ of the errors in a report
		OpenGraph graph = await OpenGraph.ParseUrlAsync(uri, validateSpecification: false);

		foreach (var @namespace in options.RequiredNamespaces)
		{
			if (!graph.Namespaces.TryGetValue(@namespace, out OpenGraphNamespace? graphNamespace))
			{
				issueList.Add($"{uri} has no metadata for {@namespace}");
				continue;
			}

			issueList = [.. issueList, .. ValidateRequiredElements(uri, graph, graphNamespace)];
		}

		foreach (var @namespace in options.OptionalNamespaces)
		{
			if (!graph.Namespaces.TryGetValue(@namespace, out OpenGraphNamespace? graphNamespace))
			{
				continue;
			}

			issueList = [.. issueList, .. ValidateRequiredElements(uri, graph, graphNamespace)];
		}

		var unexpected = graph.Namespaces
			.Where(ns => !options.RequiredNamespaces.Contains(ns.Key) && !options.OptionalNamespaces.Contains(ns.Key));

		foreach (var @namespace in unexpected)
		{
			// This will find unexpected normal OG stuff (like a game: tag when you don't ask about them),
			// but won't see misspelled or fat-fingered tags (like if you mean "profile:" and type "porfile:").
			issueList.Add($"Found unexpected namespace {@namespace} in {uri}");
		}

		if (options.ValidateImages && graph.Image != null)
		{
			var target = AdjustTarget(graph.Image, options);

			if (!await ImageExists(target, options.Verbose))
			{
				issueList.Add($"Image {target} referenced by {uri} does not exist.");
			}
		}

		return issueList;
	}

	static async Task<bool> ImageExists(Uri imageUri, bool verbose)
	{
		if (_knownImageStatus.TryGetValue(imageUri, out bool value))
		{
			return value;
		}

		if (verbose)
		{
			Console.WriteLine($"Verifying existence of {imageUri}");
		}

		// Check for image
		var status = await ValidateImageAsync(imageUri);

		if (verbose)
		{
			Console.WriteLine($"{imageUri} exists");
		}

		// Record it so we don't have to check the same URI over and over
		_knownImageStatus[imageUri] = status;

		return status;
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

	static IEnumerable<string> ValidateRequiredElements(Uri uri, OpenGraph graph, OpenGraphNamespace @namespace)
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
					yield return $"{uri} is missing required value for key {key}";
					continue;
				}
			}
			else
			{
				var metadata = graph.Metadata[key];
				if (metadata.Count == 0)
				{
					yield return $"{uri} is missing required property {targetProperty} for key {key}";
					continue;
				}

				var metadataProperties = metadata[0].Properties;
				if (metadataProperties.Count == 0)
				{
					yield return $"{uri} is missing required property {targetProperty} for key {key}";
					continue;
				}
				else
				{
					if (!metadataProperties.ContainsKey(targetProperty))
					{
						yield return $"{uri} is missing required property {targetProperty} for key {key}";
						continue;
					}
				}
			}
		}
	}

	static async Task<bool> ValidateImageAsync(Uri imageUri)
	{
		using var response = await _httpClient.GetAsync(imageUri);
		var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
		return mediaType.StartsWith("image/");
	}
}