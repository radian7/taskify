using Taskify.E2ETests;
using Taskify.TestSupport;

// One running application and one browser are shared by every test in this assembly.
[assembly: AssemblyFixture(typeof(TaskifyAppFixture))]
[assembly: AssemblyFixture(typeof(BrowserFixture))]
