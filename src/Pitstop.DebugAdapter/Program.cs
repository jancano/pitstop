// Passenger's .NET debug adapter, as the IDE carries it under debugger/ (AdapterProgram has its flags):
//   Pitstop.DebugAdapter --interpreter=vscode [--engineLogging=FILE] [--symbolCache=FOLDER] [--remote]
return await Pitstop.DebugAdapter.AdapterProgram.RunAsync(args, "Pitstop.DebugAdapter");
