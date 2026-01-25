using ConsoleAppFramework;

using PowerServe.Client;

var app = ConsoleApp.Create();
app.Add("", Commands.Root);
app.Run(args);
