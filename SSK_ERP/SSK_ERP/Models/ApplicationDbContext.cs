using SSK_ERP.Data;
using SSK_ERP.Models;
using Microsoft.AspNet.Identity.EntityFramework;
using System;
using System.Data.Entity;
using System.Data.Entity.ModelConfiguration;

namespace SSK_ERP.Models
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public DbSet<MenuMaster> MenuMasters { get; set; }
        public DbSet<RImageMaster> RImageMasters { get; set; }
        public DbSet<StateMaster> StateMasters { get; set; }
        public DbSet<RegionMaster> RegionMasters { get; set; }
        public DbSet<DesignationMaster> DesignationMasters { get; set; }
        public DbSet<DepartmentMaster> DepartmentMasters { get; set; }
        public DbSet<LocationMaster> LocationMasters { get; set; }
        public DbSet<CustomerMaster> CustomerMasters { get; set; }
        public DbSet<SupplierMaster> SupplierMasters { get; set; }
        public DbSet<CurrencyMaster> CurrencyMasters { get; set; }
        public DbSet<UnitMaster> UnitMasters { get; set; }
        public DbSet<HSNCodeMaster> HSNCodeMasters { get; set; }
        public DbSet<CostFactorMaster> CostFactorMasters { get; set; }
        public DbSet<MaterialTypeMaster> MaterialTypeMasters { get; set; }
        public DbSet<MaterialGroupMaster> MaterialGroupMasters { get; set; }
        public DbSet<MaterialMaster> MaterialMasters { get; set; }
        public DbSet<PackingMaster> PackingMasters { get; set; }
        public DbSet<TransactionMaster> TransactionMasters { get; set; }
        public DbSet<TransactionDetail> TransactionDetails { get; set; }
        public DbSet<TransactionBatchDetail> TransactionBatchDetails { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }
        new public virtual IDbSet<ApplicationRole> Roles { get; set; }
        public virtual IDbSet<Group> Groups { get; set; }
        public DbSet<CompanyMaster> companymasters { get; set; }
        public DbSet<CompanyDetail> companydetails { get; set; }
        public DbSet<TMPRPT_IDS> TMPRPT_IDS { get; set; }
        public virtual IDbSet<VW_ACCOUNTING_YEAR_DETAIL_ASSGN> VW_ACCOUNTING_YEAR_DETAIL_ASSGN { get; set; }
        public virtual IDbSet<MenuRoleMaster> MenuRoleMasters { get; set; }
        public DbSet<SoftDepartmentMaster> softdepartmentmasters { get; set; }
        public virtual IDbSet<AspNetUser> AspNetUsers { get; set; }
        public virtual IDbSet<AccountGroupMaster> accountgroupmasters { get; set; }
        public virtual IDbSet<AccountHeadMaster> accountheadmasters { get; set; }
        public virtual IDbSet<DisplayOrderMaster> displayordermasters { get; set; }
        public DbSet<EmployeeMaster> EmployeeMasters { get; set; }
        public DbSet<EmployeeLinkMaster> employeelinkmasters { get; set; }
        public DbSet<TransporterMaster> TransporterMasters { get; set; }
        // GovernmentProof module removed
        // public DbSet<GovernmentProof> GovernmentProofs { get; set; }
        public ApplicationDbContext()
         : base("SSK_DefaultConnection")
        {

        }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
            {
                throw new ArgumentNullException("modelBuilder");
            }

            // Configure CurrencyMaster decimal precision
            modelBuilder.Entity<CurrencyMaster>().Property(d => d.CURNAMT).HasPrecision(18, 2);
            
            // Configure HSNCodeMaster decimal precision for all GST fields
            modelBuilder.Entity<HSNCodeMaster>().Property(h => h.CGSTEXPRN).HasPrecision(18, 2);
            modelBuilder.Entity<HSNCodeMaster>().Property(h => h.SGSTEXPRN).HasPrecision(18, 2);
            modelBuilder.Entity<HSNCodeMaster>().Property(h => h.IGSTEXPRN).HasPrecision(18, 2);
            modelBuilder.Entity<HSNCodeMaster>().Property(h => h.ACGSTEXPRN).HasPrecision(18, 2);
            modelBuilder.Entity<HSNCodeMaster>().Property(h => h.ASGSTEXPRN).HasPrecision(18, 2);
            modelBuilder.Entity<HSNCodeMaster>().Property(h => h.AIGSTEXPRN).HasPrecision(18, 2);
            modelBuilder.Entity<HSNCodeMaster>().Property(h => h.TAXEXPRN).HasPrecision(18, 2);
            
            // Configure CostFactorMaster decimal precision for CFEXPR field
            modelBuilder.Entity<CostFactorMaster>().Property(c => c.CFEXPR).HasPrecision(18, 3);

            // Configure MaterialMaster decimal precision
            modelBuilder.Entity<MaterialMaster>().Property(m => m.MTRLPRFT).HasPrecision(18, 2);

            // Keep this:
            modelBuilder.Entity<IdentityUser>().ToTable("AspNetUsers");

            // Change TUser to ApplicationUser everywhere else - IdentityUser and ApplicationUser essentially 'share' the AspNetUsers Table in the database:
            EntityTypeConfiguration<ApplicationUser> table =
                modelBuilder.Entity<ApplicationUser>().ToTable("AspNetUsers");

            table.Property((ApplicationUser u) => u.UserName).IsRequired();
            
            // Map the CateTid property to the database column
            table.Property((ApplicationUser u) => u.CateTid).HasColumnName("CateTid");

            // EF won't let us swap out IdentityUserRole for ApplicationUserRole here:
            modelBuilder.Entity<ApplicationUser>().HasMany<IdentityUserRole>((ApplicationUser u) => u.Roles);
            modelBuilder.Entity<IdentityUserRole>().HasKey((IdentityUserRole r) =>
                new { UserId = r.UserId, RoleId = r.RoleId }).ToTable("AspNetUserRoles");


            // Add the group stuff here:
            modelBuilder.Entity<ApplicationUser>().HasMany<ApplicationUserGroup>((ApplicationUser u) => u.Groups);
            modelBuilder.Entity<ApplicationUserGroup>().HasKey((ApplicationUserGroup r) => new { UserId = r.UserId, GroupId = r.GroupId }).ToTable("ApplicationUserGroups");

            // And here:
            modelBuilder.Entity<Group>().HasMany<ApplicationRoleGroup>((Group g) => g.Roles);
            modelBuilder.Entity<ApplicationRoleGroup>().HasKey((ApplicationRoleGroup gr) => new { RoleId = gr.RoleId, GroupId = gr.GroupId }).ToTable("ApplicationRoleGroups");

            // And Here:
            EntityTypeConfiguration<Group> groupsConfig = modelBuilder.Entity<Group>().ToTable("Groups");
            groupsConfig.Property((Group r) => r.Name).IsRequired();

            // Leave this alone:
            EntityTypeConfiguration<IdentityUserLogin> entityTypeConfiguration =
                modelBuilder.Entity<IdentityUserLogin>().HasKey((IdentityUserLogin l) =>
                    new { UserId = l.UserId, LoginProvider = l.LoginProvider, ProviderKey = l.ProviderKey }).ToTable("AspNetUserLogins");

            entityTypeConfiguration.HasRequired<IdentityUser>((IdentityUserLogin u) => u.User);
            EntityTypeConfiguration<IdentityUserClaim> table1 = modelBuilder.Entity<IdentityUserClaim>().ToTable("AspNetUserClaims");
            table1.HasRequired<IdentityUser>((IdentityUserClaim u) => u.User);

            // Add this, so that IdentityRole can share a table with ApplicationRole:
            modelBuilder.Entity<IdentityRole>().ToTable("AspNetRoles");

            // Change these from IdentityRole to ApplicationRole:
            EntityTypeConfiguration<ApplicationRole> entityTypeConfiguration1 = modelBuilder.Entity<ApplicationRole>().ToTable("AspNetRoles");
            entityTypeConfiguration1.Property((ApplicationRole r) => r.Name).IsRequired();

        }

    }
}