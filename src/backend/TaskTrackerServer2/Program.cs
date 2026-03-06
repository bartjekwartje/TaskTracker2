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

    var tasks = await connection.QueryAsync<object>(tasksQuery);
    var progressData = await connection.QueryAsync<dynamic>(progressQuery);
    var runningTimer = await connection.QueryFirstOrDefaultAsync<dynamic>(runningTimerQuery);

    var progressDict = new Dictionary<string, int>();
    var durationsDict = new Dictionary<string, int>();
    foreach (var p in progressData)
    {
        string dateKey = $"{p.task_id}_{p.log_date:yyyy-MM-dd}";
        progressDict[dateKey] = p.status;
        if (p.duration_seconds > 0)
            durationsDict[dateKey] = p.duration_seconds;
    }

    object? activeTimer = runningTimer == null ? null : new
    {
        taskId = (string)runningTimer.task_id,
        baseSeconds = (int)runningTimer.base_seconds
    };

    return Results.Ok(new { tasks, progress = progressDict, durations = durationsDict, activeTimer });
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

// 7. POST: Stop a timer for a task — computes total, saves to progress, removes from running_timers
app.MapPost("/api/timers/{taskId}/stop", async (string taskId) =>
{
    using var connection = new NpgsqlConnection(connectionString);

    const string selectSql = "SELECT task_id, started_at, base_seconds FROM running_timers WHERE task_id = @TaskId";
    var timer = await connection.QueryFirstOrDefaultAsync<dynamic>(selectSql, new { TaskId = taskId });
    if (timer == null) return Results.NotFound();

    var elapsed = (int)(DateTime.UtcNow - (DateTime)timer.started_at).TotalSeconds;
    var totalSeconds = (int)timer.base_seconds + elapsed;
    var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

    const string upsertSql = @"
        INSERT INTO progress (task_id, log_date, status, duration_seconds)
        VALUES (@TaskId, @Date::date, 0, @Seconds)
        ON CONFLICT (task_id, log_date)
        DO UPDATE SET duration_seconds = EXCLUDED.duration_seconds, updated_at = CURRENT_TIMESTAMP";

    await connection.ExecuteAsync(upsertSql, new { TaskId = taskId, Date = today, Seconds = totalSeconds });

    const string deleteSql = "DELETE FROM running_timers WHERE task_id = @TaskId";
    await connection.ExecuteAsync(deleteSql, new { TaskId = taskId });

    return Results.Ok(new { totalSeconds });
});

app.Run();

// --- Data Models ---
public record TaskRequest(string Name, string GroupName, string CategoryName);
public record ProgressRequest(string TaskId, string Date, int Status);
public record MoveTaskRequest(string NewGroupName);
public record TimerStartRequest(int BaseSeconds);
