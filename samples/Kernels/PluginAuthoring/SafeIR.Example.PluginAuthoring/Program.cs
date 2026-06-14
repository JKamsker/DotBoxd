Console.WriteLine("Safe IR plugin authoring examples");

SimpleContractExamples.Run();
await KernelClassExample.RunAsync();
ManifestInspectionExample.Run();
await JsonUploadExample.RunAsync();
await HookSubscriptionExample.RunAsync();
await DesignGuidanceExample.RunAsync();
InvalidToolingExamples.Describe();
