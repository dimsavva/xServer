﻿using Microsoft.EntityFrameworkCore;
using x42.Feature.Database.Tables;

namespace x42.Feature.Database.Context
{
    class X42DbContext : DbContext
    {
        public virtual DbSet<ServerNodeData> ServerNodes { get; set; }
        public virtual DbSet<DictionaryData> DictionaryItems { get; set; }
        public virtual DbSet<ProfileData> Profiles { get; set; }
        public virtual DbSet<ProfileReservationData> ProfileReservations { get; set; }
        public virtual DbSet<PriceLockData> PriceLocks { get; set; }

        public virtual DbSet<DomainData> Domains { get; set; }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<ProfileData>()
                .HasIndex(p => new { p.Name })
                .IsUnique();
            builder.Entity<ProfileData>()
                .HasIndex(p => new { p.KeyAddress })
                .IsUnique();
            builder.Entity<ProfileData>()
                .HasIndex(p => new { p.BlockConfirmed });

            builder.Entity<ServerNodeData>()
                .HasIndex(sn => new { sn.Id })
                .IsUnique();
            builder.Entity<ServerNodeData>()
                .HasIndex(sn => new { sn.ProfileName })
                .IsUnique();

            builder.Entity<DictionaryData>()
                .HasIndex(d => new { d.Key })
                .IsUnique();

            builder.Entity<DomainData>()
                .HasIndex(p => new { p.Name })
                .IsUnique();

            builder.Entity<DomainData>()
                .HasIndex(p => new { p.KeyAddress });
 
            builder.Entity<DomainData>()
                .HasIndex(p => new { p.BlockConfirmed });
        }

        #region Initilize
        private readonly string _connectionString = "Server=127.0.0.1;Port=5432;Database=myDataBase;Integrated Security=true;";

        public X42DbContext() { }

        public X42DbContext(string connectionString)
        {
            _connectionString = connectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(_connectionString);

        }
        #endregion Initilize
    }
}