using System;
using System.ComponentModel.DataAnnotations;

namespace BreakoutOnline.Models
{
    public class UserAccount
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [StringLength(15)]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string HashedPassword { get; set; } = string.Empty;

        [Required]
        public byte[] Salt { get; set; } = new byte[16];

        public DateTime RegistrationDate { get; set; } = DateTime.UtcNow;

        public string? Title { get; set; }

        public double SurvivalHighScore { get; set; } = 0;
    }
}