namespace ControllerMagic;

// At most one open instance of a window: asking again brings the open one forward. Shown
// modeless, since a modal loop still pumps tray clicks and would open a second copy on top.
internal sealed class SingleWindow<TForm>(Func<TForm> create) : IDisposable
    where TForm : Form
{
    private TForm? _open;

    public void ShowOrActivate()
    {
        if (_open is { } open)
        {
            if (open.WindowState == FormWindowState.Minimized)
                open.WindowState = FormWindowState.Normal;
            open.Activate();
            return;
        }

        var form = create();
        // Disposed, not FormClosed: a form closed before its handle exists is only disposed.
        form.Disposed += OnDisposed;
        _open = form;
        form.Show();
    }

    public void Close() => _open?.Close();

    public void Dispose() => Close();

    private void OnDisposed(object? sender, EventArgs e)
    {
        if (sender is TForm form)
            form.Disposed -= OnDisposed;
        if (ReferenceEquals(sender, _open))
            _open = null;
    }
}
