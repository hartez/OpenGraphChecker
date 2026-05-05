# OpenGraphChecker

OpenGraphChecker is a dead-simple command-line utility to verify that a website has the required [Open Graph](https://ogp.me) elements on each page. 

OpenGraphChecker is written for .NET 10.

# Usage

Call the executable and pass the "sitemap" parameter, which should be the URL of the sitemap XML document for the site you want to check. OpenGraphChecker will download the sitemap, parse the `<loc>` tags, and then visit each location. For each page, it will grab all the Open Graph metadata and determine whether any required elements are missing. 

Example:

```console
.\OpenGraphChecker.exe -s http://localhost:5080/sitemap.xml -b http://localhost:5080 -o article profile -r article:author twitter:domain twitter:url twitter:url profile:first_name profile:last_name -v -p
```

# Parameters

## Sitemap

`-s`, `--sitemap`

Required. URL of the target site's sitemap.

## Base URL

`-b`, `--baseurl` 

Base URL for local testing. If you're testing a site in development (for example, running at `localhost`, your generate sitemap might still have live URLs. This value will be substituted for all base URLs found in the sitemap. 

For instance, my development sitemap is at `http://localhost:5080/sitemap.xml`. But the locations specified in the sitemap might look like `https://ezhart.com/posts/bye-robot`. If I'm checking my development site, then I can pass in the base URL and instead of checking `https://ezhart.com/posts/bye-robot`, OGC will check `http://localhost:5080/posts/bye-robot`. 

Example:
```console
.\OpenGraphChecker.exe -s http://localhost:5080/sitemap.xml -b http://localhost:5080 
```

## Required Elements

`-r`, `--required`

Required elements for namespaces (e.g., article:author). By default, the basic `og` namespace values are required (`og:title`, `og:image`, `og:image:alt`, `og:url`, `og:type`). Additional required elements can be added with this switch. Elements are specified in the format `namespace:element`, separated by a space. 

Example: 

```console
.\OpenGraphChecker.exe -s http://localhost:5080/sitemap.xml -r og:description twitter:description
```

## Optional Namespaces

`-o`, `--optional`     

Optional metadata namespaces. These are namespaces that might not be on every page. If the namespace is found, OGC will consider it an error if required element is missing. For example, with the command line

```console
.\OpenGraphChecker.exe -s http://localhost:5080/sitemap.xml -o article -r article:author
```

A page that doesn't have any `article` metadata would pass. A page with `article` metadata but no `article:author` element would fail. 

## Exclude Default OG Elements

`-x`, `--excludeOG`

Allows the user to disable checking for the default set of Open Graph elements. The elements can still be manually marked as required using the `-r` parameter.

## Show Progress
    
`-p`, `--progress`

If specified, the OGC will report progress as it checks URLs.

## Verbose

`-v`, `--verbose`

Shows verbose output if specified. Otherwise, only the final report will be shown.

# Credits

Command line parsing via the amazing [Command Line Parser Library](https://github.com/commandlineparser/commandline)

Open Graph parsing via [OpenGraph-Net](https://github.com/ghorsey/OpenGraph-Net)