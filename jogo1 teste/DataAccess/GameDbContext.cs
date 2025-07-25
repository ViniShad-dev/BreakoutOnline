// BreakoutOnline/DataAccess/GameDbContext.cs

using BreakoutOnline.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace BreakoutOnline.DataAccess
{
    public class GameDbContext : DbContext
    {
        public DbSet<UserAccount> Accounts { get; set; }

        public GameDbContext(DbContextOptions<GameDbContext> options) : base(options) { }
    }
}