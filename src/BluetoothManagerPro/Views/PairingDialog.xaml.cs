using System.Windows;
using System.Windows.Input;
using BluetoothManagerPro.Services;
using Windows.Devices.Enumeration;

namespace BluetoothManagerPro.Views;

/// <summary>
/// The pairing ceremony UI. Windows' own dialog is a WinRT popup that needs a CoreWindow,
/// so a desktop app has to draw this itself — which is also why the app can style it.
/// </summary>
public partial class PairingDialog : Window
{
    private readonly DevicePairingKinds _kind;

    private PairingDialog(PairingPrompt prompt)
    {
        InitializeComponent();
        _kind = prompt.Kind;

        DeviceNameText.Text = prompt.DeviceName;

        switch (prompt.Kind)
        {
            case DevicePairingKinds.ConfirmPinMatch:
                MessageText.Text = "Убедитесь, что на устройстве показан тот же код, и подтвердите сопряжение.";
                PinText.Text = prompt.Pin;
                PinDisplay.Visibility = Visibility.Visible;
                break;

            case DevicePairingKinds.DisplayPin:
                // The ceremony has already been accepted; this window only shows the code.
                MessageText.Text = "Введите этот код на устройстве.";
                PinText.Text = prompt.Pin;
                PinDisplay.Visibility = Visibility.Visible;
                AcceptButton.Content = "Готово";
                CancelButton.Visibility = Visibility.Collapsed;
                break;

            case DevicePairingKinds.ProvidePin:
                MessageText.Text = "Введите PIN-код, показанный на устройстве.";
                PinInput.Visibility = Visibility.Visible;
                Loaded += (_, _) => PinInput.Focus();
                break;

            default:
                MessageText.Text = "Подтвердите сопряжение с этим устройством.";
                break;
        }
    }

    /// <summary>Shows the dialog and reports what the user chose.</summary>
    public static Task<PairingAnswer> AskAsync(PairingPrompt prompt, Window? owner)
    {
        var dialog = new PairingDialog(prompt);
        if (owner is not null && owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        bool accepted = dialog.ShowDialog() == true;
        string? pin = dialog._kind == DevicePairingKinds.ProvidePin ? dialog.PinInput.Text.Trim() : null;

        return Task.FromResult(accepted ? PairingAnswer.Yes(pin) : PairingAnswer.No());
    }

    private void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        if (_kind == DevicePairingKinds.ProvidePin && PinInput.Text.Trim().Length == 0)
        {
            PinInput.Focus();
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnPinKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnAcceptClick(sender, e);
        }
    }
}
