using Microsoft.Extensions.DependencyInjection;
using SIT.Manager.Controls;
using SIT.Manager.ViewModels.Installation;

namespace SIT.Manager.Views.Installation;

public partial class InstallView : ActivatableUserControl
{
    public InstallView()
    {
        InitializeComponent();
        this.DataContext = App.Current.Services.GetService<InstallViewModel>();
    }
}
