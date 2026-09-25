namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// xUnit runs different test classes in parallel by default. Without this, AuthTests and
/// ScopeAuthorizationTests each get their own EduCenterWebApplicationFactory, both starting the app
/// (and its Development-only DbSeeder) concurrently — a race that trips the demo Organization's unique
/// TenantSlug constraint. Sharing one factory via this collection makes seeding run exactly once.
/// </summary>
[CollectionDefinition("Api")]
public class ApiTestCollection : ICollectionFixture<EduCenterWebApplicationFactory>;
