using Polifemo.Cli;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

return await CliApp.RunAsync(args, Console.Out, Console.Error, ct: cts.Token);
