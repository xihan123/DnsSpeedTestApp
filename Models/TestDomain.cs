using CommunityToolkit.Mvvm.ComponentModel;

namespace DNSSpeedTester.Models;

public partial class TestDomain : ObservableObject
{
    public TestDomain(string name, string domain, string category = "常用", bool isCustom = false)
    {
        _name = name;
        _domain = domain;
        _category = category;
        _isCustom = isCustom;
    }

    public TestDomain() { }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _domain = string.Empty;

    [ObservableProperty]
    private string _category = "常用";

    [ObservableProperty]
    private bool _isCustom;

    public override string ToString()
    {
        return $"{Name} [{Domain}]";
    }
}
