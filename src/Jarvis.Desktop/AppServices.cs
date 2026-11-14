using Microsoft.Extensions.DependencyInjection;

namespace Jarvis.Desktop;

public partial class App
{
    public IServiceProvider Services => _host.Services;
}
