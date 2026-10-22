// Reference only. Nothing in terraform/, ansible/, or the deploy workflows installs or
// publishes this project. S1-09 runs it on a laptop and then deletes the process.
// The file stays so the episode can be shot again.

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
var app = builder.Build();
app.MapGet("/", () => "yarp-gateway-sample");
app.MapReverseProxy();
app.Run();
