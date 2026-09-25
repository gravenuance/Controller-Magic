using System.Windows.Forms;
using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class SingleWindowTests
{
    // Records Show without creating a real window, so no test ever puts one on screen.
    private sealed class HeadlessForm : Form
    {
        public int ShowCount { get; private set; }

        protected override void SetVisibleCore(bool value)
        {
            if (value)
                ShowCount++;
        }
    }

    // WinForms controls belong on an STA thread; a private one keeps its sync context out of other tests.
    private static void OnStaThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
            throw new Xunit.Sdk.XunitException($"STA body failed: {failure}");
    }

    [Fact]
    public void ShowOrActivate_WhileOpen_ReusesTheOpenWindow() => OnStaThread(() =>
    {
        var created = new List<HeadlessForm>();
        using var window = new SingleWindow<HeadlessForm>(() =>
        {
            var form = new HeadlessForm();
            created.Add(form);
            return form;
        });

        window.ShowOrActivate();
        window.ShowOrActivate();

        Assert.Single(created);
        Assert.Equal(1, created[0].ShowCount);
    });

    [Fact]
    public void ShowOrActivate_AfterTheWindowWasClosed_OpensAFreshOne() => OnStaThread(() =>
    {
        var created = new List<HeadlessForm>();
        using var window = new SingleWindow<HeadlessForm>(() =>
        {
            var form = new HeadlessForm();
            created.Add(form);
            return form;
        });

        window.ShowOrActivate();
        window.Close();
        window.ShowOrActivate();

        Assert.Equal(2, created.Count);
        Assert.True(created[0].IsDisposed);
        Assert.False(created[1].IsDisposed);
    });
}
