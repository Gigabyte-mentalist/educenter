using EduCenter.SharedKernel;

namespace EduCenter.Modules.Organizations.Domain;

public class Organization : BaseEntity
{
    private Organization() { } // EF Core

    public Organization(string name, string tenantSlug)
    {
        Name = name;
        TenantSlug = tenantSlug;
    }

    public string Name { get; private set; } = default!;
    public string TenantSlug { get; private set; } = default!;
    /// <summary>Brand colour and logo key (<c>OrganizationBranding</c> in the API layer); null means the product's own look.</summary>
    public string? BrandingConfigJson { get; private set; }
    public string? AiLimitsConfigJson { get; private set; }
    /// <summary>Currency and working hours (<c>OrganizationSettingsCatalog</c> in the API layer); null means the catalog defaults.</summary>
    public string? SettingsJson { get; private set; }

    public void Rename(string name)
    {
        Name = name;
        MarkUpdated();
    }

    public void SetBranding(string? json)
    {
        BrandingConfigJson = json;
        MarkUpdated();
    }

    public void SetSettings(string? json)
    {
        SettingsJson = json;
        MarkUpdated();
    }

    /// <summary>Per-organization AI limits (docs/00_TZ.md §54); null means the catalog defaults.</summary>
    public void SetAiLimits(string? json)
    {
        AiLimitsConfigJson = json;
        MarkUpdated();
    }

    private readonly List<Branch> _branches = new();
    public IReadOnlyCollection<Branch> Branches => _branches;

    public Branch AddBranch(string name, string address, string timezone)
    {
        var branch = new Branch(Id, name, address, timezone);
        _branches.Add(branch);
        return branch;
    }
}
