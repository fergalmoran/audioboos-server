﻿using System.IO;
using AudioBoos.Data.Access;
using AudioBoos.Data;
using AudioBoos.Data.Interfaces;
using AudioBoos.Data.Store;
using AudioBoos.Server.Services.Email;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;

namespace AudioBoos.Server.Tests.Fixtures;

public class DbFixture : Fixture {
    private static readonly object _lock = new();
    private static bool _databaseInitialized;

    public DbFixture() {

        _serviceCollection
            .AddDbContext<AudioBoosContext>(
                options => options.UseNpgsql(
                    Config.GetConnectionString("PostgresTestsConnection")
                ));

        _serviceCollection.AddLogging();

        _serviceCollection.AddScoped<IRepository<AudioFile>, AudioFileAudioRepository>();
        _serviceCollection.AddScoped<IRepository<Artist>, ArtistAudioRepository>();
        _serviceCollection.AddScoped<IRepository<Album>, AlbumAudioRepository>();
        _serviceCollection.AddScoped<IRepository<Track>, TrackAudioRepository>();
        _serviceCollection.AddScoped<IUnitOfWork, UnitOfWork>();
        _serviceCollection.AddScoped<IEmailSender, EmailSender>();

        ServiceProvider = _serviceCollection.BuildServiceProvider();

        Seed();
    }

    private void Seed() {
        lock (_lock) {
            if (_databaseInitialized) {
                return;
            }

            using var context = ServiceProvider.GetService<AudioBoosContext>();
            if (context is null) {
                throw new NullException("Unable to get db context");
            }

            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
            context.SaveChanges();
            _databaseInitialized = true;
        }
    }
}
