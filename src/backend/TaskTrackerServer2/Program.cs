using Dapper;
using Npgsql;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Enable CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors();

// --- 2. DATABASE CONNECTION (Using Environment Variables) ---
string dbIp = Environment.GetEnvironmentVariable("DB_HOST_IP") ?? "localhost";
string dbPort = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
string dbName = Environment.GetEnvironmentVariable("DB_NAME") ?? "postgres";
string dbUser = Environment.GetEnvironmentVariable("DB_USER") ?? "postgres";
string dbPass = Environment.GetEnvironmentVariable("DB_PASS") ?? "password";

string connectionString = $"Host={dbIp};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPass}";

// Startup confirmation log
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("TaskManager API started. Listening for requests...");

// Request Logging Middleware
app.Use(async (context, next) =>
{
    logger.LogInformation("Incoming request: {Method} {Path}",
        context.Request.Method,
        context.Request.Path);
    await next();
    logger.LogInformation("Response status: {StatusCode}",
        context.Response.StatusCode);
});

// --- 5. API ENDPOINTS ---

// 1. GET: Fetch all tasks and progress
app.MapGet("/api/week-data", async () =>
{
    using var connection = new NpgsqlConnection(connectionString);

    const string tasksQuery = "SELECT id, name, group_name AS \"group\", category_name AS type FROM tasks";
    const string progressQuery = "SELECT task_id, log_date, status, duration_seconds FROM progress";
    const string runningTimerQuery = @"
        SELECT task_id, base_seconds + EXTRACT(EPOCH FROM (CURRENT_TIMESTAMP - started_at))::int AS base_seconds
        FROM running_timers LIMIT 1";
    const string notesQuery = "SELECT task_id, log_date, note FROM task_notes";

    var tasks = await connection.QueryAsync<object>(tasksQuery);
    var progressData = await connection.QueryAsync<dynamic>(progressQuery);
    var runningTimer = await connection.QueryFirstOrDefaultAsync<dynamic>(runningTimerQuery);
    var notesData = await connection.QueryAsync<dynamic>(notesQuery);

    var progressDict = new Dictionary<string, int>();
    var durationsDict = new Dictionary<string, int>();
    foreach (var p in progressData)
    {
        string dateKey = $"{p.task_id}_{p.log_date:yyyy-MM-dd}";
        progressDict[dateKey] = p.status;
        if (p.duration_seconds > 0)
            durationsDict[dateKey] = p.duration_seconds;
    }

    var notesDict = new Dictionary<string, string>();
    foreach (var n in notesData)
    {
        string dateKey = $"{n.task_id}_{n.log_date:yyyy-MM-dd}";
        notesDict[dateKey] = (string)n.note;
    }

    object? activeTimer = runningTimer == null ? null : new
    {
        taskId = (string)runningTimer.task_id,
        baseSeconds = (int)runningTimer.base_seconds
    };

    return Results.Ok(new { tasks, progress = progressDict, durations = durationsDict, notes = notesDict, activeTimer });
});

// 2. POST: Add a new task
app.MapPost("/api/tasks", async (TaskRequest req) =>
{
    using var connection = new NpgsqlConnection(connectionString);

    // Ensure the group exists first (due to FK constraints)
    const string ensureGroupSql = "INSERT INTO groups (name) VALUES (@GroupName) ON CONFLICT (name) DO NOTHING";
    await connection.ExecuteAsync(ensureGroupSql, new { req.GroupName });

    const string sql = @"
        INSERT INTO tasks (id, name, group_name, category_name) 
        VALUES (@Id, @Name, @GroupName, @CategoryName)";

    var id = Guid.NewGuid().ToString();
    await connection.ExecuteAsync(sql, new {
        Id = id,
        req.Name,
        req.GroupName,
        req.CategoryName
    });

    return Results.Created($"/api/tasks/{id}", new { id });
});

// 3. PUT: Update progress
app.MapPut("/api/progress", async (ProgressRequest req) =>
{
    using var connection = new NpgsqlConnection(connectionString);

    const string sql = @"
        INSERT INTO progress (task_id, log_date, status) 
        VALUES (@TaskId, @Date::date, @Status)
        ON CONFLICT (task_id, log_date) 
        DO UPDATE SET status = EXCLUDED.status, updated_at = CURRENT_TIMESTAMP";

    await connection.ExecuteAsync(sql, new {
        req.TaskId,
        req.Date,
        req.Status
    });

    return Results.NoContent();
});

// 4. DELETE: Remove a task
app.MapDelete("/api/tasks/{id}", async (string id) =>
{
    using var connection = new NpgsqlConnection(connectionString);
    const string sql = "DELETE FROM tasks WHERE id = @id";
    await connection.ExecuteAsync(sql, new { id });
    return Results.NoContent();
});

// 5a. PUT: Change a task's type/category
app.MapPut("/api/tasks/{id}/type", async (string id, ChangeTypeRequest req) =>
{
    using var connection = new NpgsqlConnection(connectionString);
    const string sql = "UPDATE tasks SET category_name = @CategoryName WHERE id = @Id";
    var rows = await connection.ExecuteAsync(sql, new { Id = id, req.CategoryName });
    return rows == 0 ? Results.NotFound() : Results.NoContent();
});

// 5. PUT: Move a task to a different group
app.MapPut("/api/tasks/{id}/move", async (string id, MoveTaskRequest req) =>
{
    using var connection = new NpgsqlConnection(connectionString);

    // First, ensure the new group exists
    const string ensureGroupSql = "INSERT INTO groups (name) VALUES (@GroupName) ON CONFLICT (name) DO NOTHING";
    await connection.ExecuteAsync(ensureGroupSql, new { GroupName = req.NewGroupName });

    // Then update the task's group
    const string updateTaskSql = @"
        UPDATE tasks
        SET group_name = @NewGroupName
        WHERE id = @Id";

    var rowsAffected = await connection.ExecuteAsync(updateTaskSql, new {
        Id = id,
        NewGroupName = req.NewGroupName
    });

    if (rowsAffected == 0)
    {
        return Results.NotFound($"Task with ID {id} not found.");
    }

    return Results.NoContent();
});

// 6. POST: Start a timer for a task
app.MapPost("/api/timers/{taskId}/start", async (string taskId, TimerStartRequest req) =>
{
    using var connection = new NpgsqlConnection(connectionString);

    const string sql = @"
        INSERT INTO running_timers (task_id, started_at, base_seconds)
        VALUES (@TaskId, CURRENT_TIMESTAMP, @BaseSeconds)
        ON CONFLICT (task_id)
        DO UPDATE SET started_at = CURRENT_TIMESTAMP, base_seconds = EXCLUDED.base_seconds";

    await connection.ExecuteAsync(sql, new { TaskId = taskId, req.BaseSeconds });
    return Results.NoContent();
});

// 7. POST: Stop a timer for a task — saves client-computed total to progress, removes from running_timers
app.MapPost("/api/timers/{taskId}/stop", async (string taskId, TimerStopRequest req) =>
{
    using var connection = new NpgsqlConnection(connectionString);

    var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

    const string upsertSql = @"
        INSERT INTO progress (task_id, log_date, status, duration_seconds)
        VALUES (@TaskId, @Date::date, 0, @Seconds)
        ON CONFLICT (task_id, log_date)
        DO UPDATE SET duration_seconds = EXCLUDED.duration_seconds, updated_at = CURRENT_TIMESTAMP";

    await connection.ExecuteAsync(upsertSql, new { TaskId = taskId, Date = today, Seconds = req.TotalSeconds });

    const string deleteSql = "DELETE FROM running_timers WHERE task_id = @TaskId";
    await connection.ExecuteAsync(deleteSql, new { TaskId = taskId });

    return Results.NoContent();
});

// 8. PUT: Upsert a note for a task+date
app.MapPut("/api/notes", async (NoteRequest req) =>
{
    using var connection = new NpgsqlConnection(connectionString);

    const string sql = @"
        INSERT INTO task_notes (task_id, log_date, note)
        VALUES (@TaskId, @Date::date, @Note)
        ON CONFLICT (task_id, log_date)
        DO UPDATE SET note = EXCLUDED.note, updated_at = CURRENT_TIMESTAMP";

    await connection.ExecuteAsync(sql, new { req.TaskId, req.Date, req.Note });
    return Results.NoContent();
});

// 9. GET: Fetch full history for a task — every day from created_at to today
app.MapGet("/api/tasks/{id}/history", async (string id) =>
{
    using var connection = new NpgsqlConnection(connectionString);

    const string sql = @"
        SELECT
            d.day::text AS date,
            COALESCE(p.status, 0) AS status,
            COALESCE(p.duration_seconds, 0) AS duration_seconds,
            COALESCE(n.note, '') AS note
        FROM generate_series(
            (SELECT created_at::date FROM tasks WHERE id = @Id),
            CURRENT_DATE,
            '1 day'::interval
        ) AS d(day)
        LEFT JOIN progress p ON p.task_id = @Id AND p.log_date = d.day::date
        LEFT JOIN task_notes n ON n.task_id = @Id AND n.log_date = d.day::date
        ORDER BY d.day DESC";

    var rows = await connection.QueryAsync<dynamic>(sql, new { Id = id });
    return Results.Ok(rows);
});

app.Run();

// --- Data Models ---
public record TaskRequest(string Name, string GroupName, string CategoryName);
public record ProgressRequest(string TaskId, string Date, int Status);
public record MoveTaskRequest(string NewGroupName);
public record ChangeTypeRequest(string CategoryName);
public record TimerStartRequest(int BaseSeconds);
public record TimerStopRequest(int TotalSeconds);
public record NoteRequest(string TaskId, string Date, string Note);
