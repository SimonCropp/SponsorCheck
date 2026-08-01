var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<ClipboardService>();
builder.Services.AddScoped(_ => new HttpClient());
builder.Services.AddScoped<PackageLookup>();

await builder.Build().RunAsync();
