namespace DNSSpeedTester.Models;

public class TestDomain
{
    public TestDomain(string name, string domain, string category = "常用", bool isCustom = false)
    {
        Name = name;
        Domain = domain;
        Category = category;
        IsCustom = isCustom;
    }

    public TestDomain()
    {
    }

    public string Name { get; set; }
    public string Domain { get; set; }
    public string Category { get; set; }
    public bool IsCustom { get; set; }

    public override string ToString()
    {
        return $"{Name} [{Domain}]";
    }
}