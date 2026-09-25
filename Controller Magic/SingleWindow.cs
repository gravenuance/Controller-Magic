namespace ControllerMagic;

// At most one instance of a window, built once and kept: the user closing it only hides it, and
// asking again shows that same instance. Close() really closes it, for app shutdown. Shown
// modeless, since a modal loop still pumps tray clicks and would open a second copy on top.
internal sealed class SingleWindow<TForm>(Func<TForm> create) : IDisposable
    where TForm : Form
{
    private TForm? _open;
    private bool _hidden;
    private bool _closingForReal;

    public void ShowOrActivate()
    {
        if (_open is { } open)
        {
            if (_hidden)
            {
                _hidden = false;
                open.Show();
            }
            if (open.WindowState == FormWindowState.Minimized)
                open.WindowState = FormWindowState.Normal;
            open.Activate();
            return;
        }

        var form = create();
        // Disposed, not FormClosed: a form closed before its handle exists is only disposed.
        form.Disposed += OnDisposed;
        form.FormClosing += OnFormClosing;
        _open = form;
        form.Show();
    }

    public void Close()
    {
        _closingForReal = true;
        try
        {
            _open?.Close();
        }
        finally
        {
            _closingForReal = false;
        }
    }

    public void Dispose() => Close();

    // Only a user close becomes a hide; Windows shutting down or ending the task must really close.
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closingForReal || e.CloseReason != CloseReason.UserClosing || sender is not TForm form)
            return;

        e.Cancel = true;
        _hidden = true;
        form.Hide();
    }

    private void OnDisposed(object? sender, EventArgs e)
    {
        if (sender is TForm form)
        {
            form.Disposed -= OnDisposed;
            form.FormClosing -= OnFormClosing;
        }
        if (ReferenceEquals(sender, _open))
        {
            _open = null;
            _hidden = false;
        }
    }
}
