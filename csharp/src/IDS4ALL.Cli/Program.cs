using IDS4ALL.Cli;
using IDS4ALL.IfcTester;

return Converter.Run(args, Console.Out, Console.Error,
    () => new IdsWriter(PythonRuntimeOptions.FromEnvironment()), DateOnly.FromDateTime(DateTime.Now));
