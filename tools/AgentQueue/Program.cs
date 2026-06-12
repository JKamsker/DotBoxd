using AgentQueue;

return new AgentQueueApp(Console.Out, Console.Error, SystemClock.Instance)
    .Run(args, Environment.CurrentDirectory);
