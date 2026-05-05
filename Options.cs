using CommandLine;

internal partial class Program
{
	public class Options
	{
		private IEnumerable<string> _elements = [];
		private readonly string[] _ogElements = ["og:title", "og:type", "og:image", "og:image:alt", "og:url"];

		[Option('s', "sitemap", Required = true, HelpText = "URL of the target site's sitemap")]
		public string Sitemap { get; set; } = "";

		[Option('b', "baseurl", Required = false, HelpText = "Base URL for local testing")]
		public string LocalBaseUrl { get; set; } = "";

		[Option('r', "required", Required = false, HelpText = "Required elements for namespaces (e.g., article:author)", Separator = ' ')]
		public IEnumerable<string> Elements
		{
			get => _elements;
			set
			{
				if (ExcludeOG)
				{
					_elements = value;
				}
				else
				{
					_elements = value.Concat(_ogElements);
				}
			}
		}

		[Option('o', "optional", Required = false, HelpText = "Optional metadata namespaces", Separator = ' ')]
		public IEnumerable<String> OptionalNamespaces { get; set; } = [];

		[Option('x', "excludeOG", Required = false, HelpText = "Exclude Open Graph \"title\", \"type\", \"image\", \"url\" (required by default)")]
		public bool ExcludeOG { get; set; }

		[Option('p', "progress", Required = false, HelpText = "Report progress while checking")]
		public bool Progress { get; set; }

		[Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages")]
		public bool Verbose { get; set; }

		public IEnumerable<KeyValuePair<string, string>> RequiredElements => Elements.Select(e =>
		{
			var index = e.IndexOf(':');
			return KeyValuePair.Create(e[..index], e[(index + 1)..]);
		});

		public IEnumerable<string> RequiredNamespaces => RequiredElements.Select(re => re.Key)
			.Where(ns => !OptionalNamespaces.Contains(ns))
			.Distinct();

		public void OutputOptions()
		{
			if (Verbose)
			{
				Console.WriteLine($"Target sitemap is {Sitemap}");

				if (!string.IsNullOrEmpty(LocalBaseUrl))
				{
					Console.WriteLine($"URLs from sitemap will be retargeted at base URL {LocalBaseUrl}");
				}

				if (ExcludeOG)
				{
					Console.WriteLine("Default Open Graph required values (title, type, image, url) are excluded from check");
				}

				Console.WriteLine($"Optional namespaces are {String.Join(", ", OptionalNamespaces)}");

				Console.WriteLine($"Required elements for each namespace are {String.Join(", ", RequiredElements.Select(ToOutput))}");
			}
		}

		private string ToOutput(KeyValuePair<string, string> pair)
		{
			return $"{pair.Key}:{pair.Value}";
		}
	}
}