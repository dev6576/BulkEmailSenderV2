using Microsoft.EntityFrameworkCore;

namespace BulkEmailSender.Api.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options)
{
}