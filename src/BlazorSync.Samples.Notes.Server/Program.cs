using BlazorSync.Documents;
using BlazorSync.Protocol;
using BlazorSync.Samples.Shared;
using BlazorSync.Server;
using BlazorSync.Server.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// The sample keeps server data in memory: it is lost when the server restarts. Replicas notice (new epoch),
// reset, and push their pending notes again. A production server uses a database-backed authority and
// authenticates callers (see docs/architecture/adr-009 and adr-010).
var authority = new InMemorySyncServer<Note>(new InMemorySyncServerOptions<Note>
{
    Cloner = DocumentCloner.Json(NotesJson.Default.Note),
    Fingerprint = DocumentCloner.JsonFingerprint(NotesJson.Default.Note),
});

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapSyncCollection("notes", authority, SyncJsonTypes<Note>.From(NotesJson.Default), new SyncEndpointOptions
{
    SupportedSchemas = new HashSet<string>(StringComparer.Ordinal) { NotesJson.SchemaId },
});
app.MapFallbackToFile("index.html");

app.Run();
