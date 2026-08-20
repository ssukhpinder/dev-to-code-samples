using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

const string MissingDepartment = "[missing]";
const string VacantDepartment = "[vacant]";

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

var options = new DbContextOptionsBuilder<DirectoryDbContext>()
    .UseSqlite(connection)
    .Options;

await using (var seedContext = new DirectoryDbContext(options))
{
    await seedContext.Database.EnsureCreatedAsync();

    seedContext.Departments.AddRange(
        new Department(1, "Platform"),
        new Department(2, "Sales"));

    seedContext.Employees.AddRange(
        new Employee(1, "Ada", 1),
        new Employee(2, "Lin", 99));

    await seedContext.SaveChangesAsync();
}

await using var context = new DirectoryDbContext(options);

var employeeDirectoryQuery = context.Employees
    .LeftJoin(
        context.Departments,
        employee => employee.DepartmentId,
        department => department.Id,
        (employee, department) => new
        {
            Employee = employee.Name,
            Department = department == null ? MissingDepartment : department.Name,
        })
    .OrderBy(row => row.Employee);

var staffingDirectoryQuery = context.Employees
    .RightJoin(
        context.Departments,
        employee => employee.DepartmentId,
        department => department.Id,
        (employee, department) => new
        {
            Department = department.Name,
            Employee = employee == null ? VacantDepartment : employee.Name,
        })
    .OrderBy(row => row.Department);

var leftSql = employeeDirectoryQuery.ToQueryString();
var rightSql = staffingDirectoryQuery.ToQueryString();
var employeeRows = (await employeeDirectoryQuery.ToArrayAsync())
    .Select(row => new EmployeeDepartment(row.Employee, row.Department))
    .ToArray();
var staffingRows = (await staffingDirectoryQuery.ToArrayAsync())
    .Select(row => new DepartmentEmployee(row.Department, row.Employee))
    .ToArray();

var checks = new[]
{
    new Verification(
        "LeftJoin translates to LEFT JOIN",
        leftSql.Contains("LEFT JOIN", StringComparison.OrdinalIgnoreCase)),
    new Verification(
        "LeftJoin keeps the matched employee",
        employeeRows.Contains(new EmployeeDepartment("Ada", "Platform"))),
    new Verification(
        "LeftJoin keeps the orphaned employee",
        employeeRows.Contains(new EmployeeDepartment("Lin", MissingDepartment))),
    new Verification(
        "LeftJoin returns the expected rows",
        employeeRows.SequenceEqual(
            [
                new EmployeeDepartment("Ada", "Platform"),
                new EmployeeDepartment("Lin", MissingDepartment),
            ])),
    new Verification(
        "RightJoin translates to RIGHT JOIN",
        rightSql.Contains("RIGHT JOIN", StringComparison.OrdinalIgnoreCase)),
    new Verification(
        "RightJoin keeps the unstaffed department",
        staffingRows.Contains(new DepartmentEmployee("Sales", VacantDepartment))),
    new Verification(
        "RightJoin returns the expected rows",
        staffingRows.SequenceEqual(
            [
                new DepartmentEmployee("Platform", "Ada"),
                new DepartmentEmployee("Sales", VacantDepartment),
            ])),
};

var passed = 0;
foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
    passed += check.Passed ? 1 : 0;
}

Console.WriteLine($"Verifier: {passed}/{checks.Length} passed");
return passed == checks.Length ? 0 : 1;

internal sealed class DirectoryDbContext(DbContextOptions<DirectoryDbContext> options)
    : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<Department> Departments => Set<Department>();
}

internal sealed class Employee(int id, string name, int departmentId)
{
    public int Id { get; init; } = id;

    public string Name { get; init; } = name;

    public int DepartmentId { get; init; } = departmentId;
}

internal sealed class Department(int id, string name)
{
    public int Id { get; init; } = id;

    public string Name { get; init; } = name;
}

internal sealed record EmployeeDepartment(string Employee, string Department);

internal sealed record DepartmentEmployee(string Department, string Employee);

internal sealed record Verification(string Name, bool Passed);
