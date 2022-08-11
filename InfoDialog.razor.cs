
namespace Web.Components;

using System;
using Microsoft.AspNetCore.Components;
using ViewModel.Library.Abstractions;
using Web.ViewModels;

/// <summary>
/// An info dialog for showing report info.
/// </summary>
public partial class InfoDialog : IDisposable
{
    /// <summary>
    /// Stores whether this component is disposed.
    /// </summary>
    private bool isDisposed;

    private Dialog dialog;

    /// <summary>
    /// Gets or sets The InfoDialog view model.
    /// </summary>
    [Inject]
    public IInfoDialog ViewModel { get; set; }

    /// <summary>
    /// Gets the wrapper for SfDialog.
    /// </summary>
    public SfDialog Dialog => this.dialog;

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    /// <param name="isDisposing">Indicates whether the object is being disposed.</param>
    protected virtual void Dispose(bool isDisposing)
    {
        if (this.isDisposed)
        {
            return;
        }

        if (isDisposing)
        {
            // The dispose method is not available for the Sf dialog
            // this.dialog?.Dispose();
        }

        this.isDisposed = true;
    }

  /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        this.ViewModel.NavItem = this.NavItem;
    }
}
