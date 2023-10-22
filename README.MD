# PowerServe

Run pwsh scripts repeatedly in a high performance manner and get JSONified results.

## Quick Start

Download the release and extract into a folder.

`PowerClient.exe "MyScript"`

Note: This will spawn a background pwsh.exe process. To end that process when finished, use the following command:
`get-process pwsh | where commandline -match 'Start-PowerServe' | stop-process`

However it is purposefully meant to be persistent to "cache" the startup and runspace pools for better efficiency, so you would generally only do this for troubleshooting and testing.

## Purpose

This is primarily targeted at monitoring programs such as PRTG that execute pwsh.exe dozens if not hundreds of times a minute, in an effort to reduce the memory and overhead of all those separate processes. It also has a good use for other programming languages being able to call into PWSH and get a JSON result quickly and simply.

## Architecture

PowerClient.exe is a .NET 8 AOT-optimized native executable that takes a script argument and then:

1. Attempts to connect to the PowerServe server via a named pipe
1. If the server is not running, start a pwsh.exe process and inject the PowerServe server into it, loading it as a module and opening the named pipe for the server.
1. Encode the script in base64 and send it as a single line to the server via the named pipe
1. Server takes the script, decodes it, and executes it in a runspace pool
1. It will take the results, serialized them to a single-lined JSON, and then return it to the client via the named pipe which in turn outputs that via stdout.
1. If a single string is returned from the script, it is passed through (removing any newlines). This is done in the case the script has already done the JSON serialization or wants to return an alternate output.

## Debugging

You can alternatively for debugging purposes start a pwsh.exe process and then run:
`import-module path/to/powerserve.dll;Start-PowerServe -PipeName PowerServe-USERNAME`

Replacing USERNAME with your current username. If you do this then you will be able to see the logs for the normally backgrounded process. The client will then attach to this server.

## Roadmap

* Controlling number of threads
* "Script Affinity" which will attempt to re-run the same script in the same runspace and minimize memory usage and startup time
* More command line options to control behavior
