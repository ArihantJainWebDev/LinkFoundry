using System.ComponentModel;
using System.Net.NetworkInformation;

namespace PersonalDownloader.Desktop;

public sealed class RackSlot(Adapter adapter) : INotifyPropertyChanged
{
    int lane = -1;
    public string Id => adapter.Id;
    public string Name => adapter.Name;
    public int HardwareLane => Artwork.EndsWith("Router.png") ? 0 : Artwork.EndsWith("Phone.png") ? 1 : 2;
    public string Selection => lane < 0 ? "Available" : $"Lane {(char)('A' + lane)}  ·  Selected";
    public string Accent => lane < 0 ? "#A7BBC7" : HardwareLane switch { 0 => "#BFF773", 1 => "#73DCFA", _ => "#F5B96E" };
    public string Artwork
    {
        get
        {
            string model = "Wired";
            if (adapter.Type == NetworkInterfaceType.Wireless80211) model = "Router";
            else if (adapter.Name.Contains("tether", StringComparison.OrdinalIgnoreCase) ||
                     adapter.Description.Contains("Remote NDIS", StringComparison.OrdinalIgnoreCase) ||
                     adapter.Description.Contains("RNDIS", StringComparison.OrdinalIgnoreCase) ||
                     adapter.Description.Contains("Apple Mobile Device", StringComparison.OrdinalIgnoreCase)) model = "Phone";
            return $"/PersonalDownloader.Desktop;component/Assets/{model}.png";
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Select(int index)
    {
        if (lane == index) return;
        lane = index;
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(""));
    }
    public override string ToString() => Name;
}
