using Taskify.TestSupport;

// One running application (AppHost, PostgreSQL, all services) is shared by every test in this assembly.
[assembly: AssemblyFixture(typeof(TaskifyAppFixture))]
