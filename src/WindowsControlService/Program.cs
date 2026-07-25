var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.Run();

/// <summary>Entry point, made accessible so the integration tests can host it.</summary>
public partial class Program;
