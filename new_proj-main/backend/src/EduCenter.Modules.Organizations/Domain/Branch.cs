using EduCenter.SharedKernel;

namespace EduCenter.Modules.Organizations.Domain;

public class Branch : TenantEntity
{
    private Branch() { } // EF Core

    public Branch(Guid organizationId, string name, string address, string timezone)
    {
        OrganizationId = organizationId;
        Name = name;
        Address = address;
        Timezone = timezone;
    }

    public string Name { get; private set; } = default!;
    public string Address { get; private set; } = default!;
    public string Timezone { get; private set; } = default!;

    public void Update(string name, string address, string timezone)
    {
        Name = name;
        Address = address;
        Timezone = timezone;
        MarkUpdated();
    }
}
