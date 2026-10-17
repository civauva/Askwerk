// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Messages;
using Askwerk.Domain.Assets;
using Askwerk.Domain.Themes;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Instances;
using Askwerk.Domain.Integrations;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Infrastructure.Persistence;

/// <summary>The one database context. Table and column names are snake_case, set explicitly.</summary>
public sealed class AskwerkDbContext(DbContextOptions<AskwerkDbContext> options) : DbContext(options)
{
    /// <summary>The installation record (at most one row).</summary>
    public DbSet<Instance> Instances => Set<Instance>();

    /// <summary>What setup recorded about the installation (at most one row).</summary>
    public DbSet<InstanceSettings> InstanceSettings => Set<InstanceSettings>();

    /// <summary>Organisations.</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>Accounts, one per person across the installation.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Passkeys of users.</summary>
    public DbSet<UserPasskey> Passkeys => Set<UserPasskey>();

    /// <summary>Who belongs to which tenant, with which role.</summary>
    public DbSet<Membership> Memberships => Set<Membership>();

    /// <summary>Gets the organisations' OAuth clients (AWK-32).</summary>
    public DbSet<OAuthClient> OAuthClients => Set<OAuthClient>();

    /// <summary>Gets the OAuth clients' access tokens (AWK-32).</summary>
    public DbSet<AccessToken> AccessTokens => Set<AccessToken>();

    /// <summary>Gets the organisations' webhooks (AWK-32).</summary>
    public DbSet<Webhook> Webhooks => Set<Webhook>();

    /// <summary>Gets the webhooks' deliveries (AWK-32).</summary>
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    /// <summary>Gets the open invitations into organisations (AWK-30).</summary>
    public DbSet<MemberInvitation> MemberInvitations => Set<MemberInvitation>();

    /// <summary>Gets how each organisation lets people sign in besides Askwerk accounts (AWK-12).</summary>
    public DbSet<TenantSignIn> TenantSignIns => Set<TenantSignIn>();

    /// <summary>Gets the host users known in each organisation (AWK-12).</summary>
    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();

    /// <summary>Gets the sessions of the designer and Results inside host products (AWK-13).</summary>
    public DbSet<EmbedSession> EmbedSessions => Set<EmbedSession>();

    /// <summary>Gets the hand-over tokens already used, until they expire (AWK-12).</summary>
    public DbSet<HandoverTokenUse> HandoverTokenUses => Set<HandoverTokenUse>();

    /// <summary>Surveys of the organisations.</summary>
    public DbSet<Survey> Surveys => Set<Survey>();

    /// <summary>Versions of the surveys' definitions.</summary>
    public DbSet<SurveyVersion> SurveyVersions => Set<SurveyVersion>();

    /// <summary>Pages of published versions.</summary>
    public DbSet<SurveyPageIndex> SurveyPages => Set<SurveyPageIndex>();

    /// <summary>Questions of published versions.</summary>
    public DbSet<SurveyQuestionIndex> SurveyQuestions => Set<SurveyQuestionIndex>();

    /// <summary>Options and matrix rows of published questions.</summary>
    public DbSet<SurveyOptionIndex> SurveyOptions => Set<SurveyOptionIndex>();

    /// <summary>Renamed variables.</summary>
    public DbSet<VariableMapping> VariableMappings => Set<VariableMapping>();

    /// <summary>Gets the approvals of survey drafts (AWK-28).</summary>
    public DbSet<SurveyApproval> SurveyApprovals => Set<SurveyApproval>();

    /// <summary>The organisations' own domains (AWK-7).</summary>
    public DbSet<Askwerk.Domain.Domains.CustomDomain> Domains => Set<Askwerk.Domain.Domains.CustomDomain>();

    /// <summary>People invited to surveys (AWK-7).</summary>
    public DbSet<Askwerk.Domain.Participants.Participant> Participants => Set<Askwerk.Domain.Participants.Participant>();

    /// <summary>The organisations' address books (W35).</summary>
    public DbSet<Askwerk.Domain.Participants.Contact> Contacts => Set<Askwerk.Domain.Participants.Contact>();

    /// <summary>Sendings to lists of participants (AWK-7).</summary>
    public DbSet<Askwerk.Domain.Participants.Invitation> Invitations => Set<Askwerk.Domain.Participants.Invitation>();

    /// <summary>Addresses that asked for no more invitations (AWK-7).</summary>
    public DbSet<Askwerk.Domain.Participants.Unsubscribe> Unsubscribes => Set<Askwerk.Domain.Participants.Unsubscribe>();

    /// <summary>Background work (AWK-7).</summary>
    public DbSet<Askwerk.Domain.Jobs.Job> Jobs => Set<Askwerk.Domain.Jobs.Job>();

    /// <summary>Responses to published versions.</summary>
    public DbSet<SurveyResponse> Responses => Set<SurveyResponse>();

    /// <summary>Answers, one row per response and question.</summary>
    public DbSet<Answer> Answers => Set<Answer>();

    /// <summary>Themes of the organisations (AWK-17).</summary>
    public DbSet<Theme> Themes => Set<Theme>();

    /// <summary>Message texts of the organisations (AWK-17).</summary>
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();

    /// <summary>Survey templates the organisations saved (AWK-17).</summary>
    public DbSet<SurveyTemplate> SurveyTemplates => Set<SurveyTemplate>();

    /// <summary>Uploaded files of the organisations (AWK-17).</summary>
    public DbSet<Asset> Assets => Set<Asset>();

    /// <summary>Audit entries, append-only for the app role.</summary>
    public DbSet<AuditLogRow> AuditLog => Set<AuditLogRow>();

    /// <summary>The organisations' keys for the Public API (AWK-25).</summary>
    public DbSet<Askwerk.Domain.Integrations.ApiKey> ApiKeys => Set<Askwerk.Domain.Integrations.ApiKey>();

    /// <summary>Results exports prepared by jobs (AWK-25).</summary>
    public DbSet<Askwerk.Domain.Integrations.DataExport> DataExports => Set<Askwerk.Domain.Integrations.DataExport>();

    /// <summary>
    /// Finds an API key by its first 12 characters before the organisation is known: <c>api_key_lookup</c> is a
    /// SECURITY DEFINER function that sees across organisations and returns that one key only.
    /// </summary>
    /// <param name="prefix">The key's first 12 characters.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The key, untracked, or null.</returns>
    public Task<Askwerk.Domain.Integrations.ApiKey?> FindApiKeyAsync(string prefix, CancellationToken cancellationToken) =>
        ApiKeys.FromSql($"SELECT * FROM api_key_lookup({prefix})").AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    /// <summary>Audit content written in this unit of work, chained and stored on save.</summary>
    internal List<AuditContent> PendingAudit { get; } = [];

    /// <summary>Creates a context for the given connection string.</summary>
    /// <param name="connectionString">A PostgreSQL connection string.</param>
    /// <returns>A new context; the caller disposes it.</returns>
    public static AskwerkDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AskwerkDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AskwerkDbContext(options);
    }

    /// <summary>Creates a context whose connections carry the session's tenant and user for row-level security.</summary>
    /// <param name="connectionString">A PostgreSQL connection string.</param>
    /// <param name="session">Who the request acts for.</param>
    /// <returns>A new context; the caller disposes it.</returns>
    public static AskwerkDbContext Create(string connectionString, DbSessionContext session)
    {
        var options = new DbContextOptionsBuilder<AskwerkDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(new SessionContextInterceptor(session))
            .Options;

        return new AskwerkDbContext(options);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Instance>(instance =>
        {
            instance.ToTable("instance");
            instance.HasKey(row => row.Id);
            instance.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            instance.Property(row => row.InitializedAt).HasColumnName("initialized_at");
        });

        modelBuilder.Entity<InstanceSettings>(settings =>
        {
            settings.ToTable("instance_settings");
            settings.HasKey(row => row.InstanceId);
            settings.Property(row => row.InstanceId).HasColumnName("instance_id").ValueGeneratedNever();
            settings.Property(row => row.Name).HasColumnName("name").HasMaxLength(200);
            settings.Property(row => row.BaseAddress).HasColumnName("base_address").HasMaxLength(500);
            settings.Property(row => row.DefaultLanguage).HasColumnName("default_language").HasMaxLength(16);
            settings.Property(row => row.TimeZone).HasColumnName("time_zone").HasMaxLength(64);
            settings.Property(row => row.OwnerUserId).HasColumnName("owner_user_id");
            settings.Property(row => row.CompletedAt).HasColumnName("completed_at");
            settings.Property(row => row.SmtpHost).HasColumnName("smtp_host").HasMaxLength(255);
            settings.Property(row => row.SmtpPort).HasColumnName("smtp_port");
            settings.Property(row => row.SmtpUserName).HasColumnName("smtp_user_name").HasMaxLength(320);
            settings.Property(row => row.SmtpPassword).HasColumnName("smtp_password");
            settings.Property(row => row.SmtpSender).HasColumnName("smtp_sender").HasMaxLength(400);
            settings.HasOne<Instance>().WithOne().HasForeignKey<InstanceSettings>(row => row.InstanceId);
            settings.HasOne<User>().WithMany().HasForeignKey(row => row.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.ToTable("tenant");
            tenant.HasKey(row => row.Id);
            tenant.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            tenant.Property(row => row.Name).HasColumnName("name").HasMaxLength(200);
            tenant.Property(row => row.Slug).HasColumnName("slug").HasMaxLength(40);
            tenant.Property(row => row.CreatedAt).HasColumnName("created_at");
            tenant.Property(row => row.EmbedOrigins).HasColumnName("embed_origins").HasDefaultValueSql("'{}'");
            tenant.Property(row => row.DesignerEmbedOrigins).HasColumnName("designer_embed_origins").HasDefaultValueSql("'{}'");
            tenant.Property(row => row.Status).HasColumnName("status").HasConversion(LowerCase<TenantStatus>()).HasMaxLength(16).HasDefaultValue(TenantStatus.Active);
            tenant.Property(row => row.DeletedAt).HasColumnName("deleted_at");
            tenant.Property(row => row.PurgedAt).HasColumnName("purged_at");
            tenant.Ignore(row => row.RemovesAt);
            tenant.HasIndex(row => row.Slug).IsUnique();
        });

        modelBuilder.Entity<User>(user =>
        {
            // "user" is reserved in PostgreSQL.
            user.ToTable("app_user");
            user.HasKey(row => row.Id);
            user.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            user.Property(row => row.Email).HasColumnName("email").HasMaxLength(320);
            user.Property(row => row.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(320);
            user.Property(row => row.DisplayName).HasColumnName("display_name").HasMaxLength(200);
            user.Property(row => row.PasswordHash).HasColumnName("password_hash");
            user.Property(row => row.SecurityStamp).HasColumnName("security_stamp").HasMaxLength(64);
            user.Property(row => row.TwoFactorEnabled).HasColumnName("two_factor_enabled");
            user.Property(row => row.AuthenticatorKey).HasColumnName("authenticator_key");
            user.Property(row => row.RecoveryCodes).HasColumnName("recovery_codes");
            user.Property(row => row.AccessFailedCount).HasColumnName("access_failed_count");
            user.Property(row => row.LockoutEnd).HasColumnName("lockout_end");
            user.Property(row => row.Language).HasColumnName("language").HasMaxLength(8);
            user.Property(row => row.Theme).HasColumnName("theme").HasMaxLength(8).HasDefaultValue("system");
            user.Property(row => row.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(16);
            user.Property(row => row.CreatedAt).HasColumnName("created_at");
            user.Property(row => row.DeactivatedAt).HasColumnName("deactivated_at");
            user.Property(row => row.LastSignInAt).HasColumnName("last_sign_in_at");
            user.Property(row => row.IsInstallationAdmin).HasColumnName("is_installation_admin").HasDefaultValue(false);
            user.HasIndex(row => row.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<UserPasskey>(passkey =>
        {
            passkey.ToTable("user_passkey");
            passkey.HasKey(row => row.CredentialId);
            passkey.Property(row => row.CredentialId).HasColumnName("credential_id");
            passkey.Property(row => row.UserId).HasColumnName("user_id");
            passkey.Property(row => row.PublicKey).HasColumnName("public_key");
            passkey.Property(row => row.SignCount).HasColumnName("sign_count");
            passkey.Property(row => row.Transports).HasColumnName("transports");
            passkey.Property(row => row.IsUserVerified).HasColumnName("is_user_verified");
            passkey.Property(row => row.IsBackupEligible).HasColumnName("is_backup_eligible");
            passkey.Property(row => row.IsBackedUp).HasColumnName("is_backed_up");
            passkey.Property(row => row.AttestationObject).HasColumnName("attestation_object");
            passkey.Property(row => row.ClientDataJson).HasColumnName("client_data_json");
            passkey.Property(row => row.Name).HasColumnName("name").HasMaxLength(100);
            passkey.Property(row => row.CreatedAt).HasColumnName("created_at");
            passkey.Property(row => row.LastUsedAt).HasColumnName("last_used_at");
            passkey.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
            passkey.HasIndex(row => row.UserId);
        });

        modelBuilder.Entity<OAuthClient>(client =>
        {
            client.ToTable("oauth_client");
            client.HasKey(row => row.Id);
            client.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            client.Property(row => row.TenantId).HasColumnName("tenant_id");
            client.Property(row => row.Name).HasColumnName("name").HasMaxLength(100);
            client.Property(row => row.ClientId).HasColumnName("client_id").HasMaxLength(32);
            client.Property(row => row.SecretHash).HasColumnName("secret_hash").HasMaxLength(64);
            client.Property(row => row.Scopes).HasColumnName("scopes");
            client.Property(row => row.ExpiresAt).HasColumnName("expires_at");
            client.Property(row => row.RevokedAt).HasColumnName("revoked_at");
            client.Property(row => row.CreatedBy).HasColumnName("created_by");
            client.Property(row => row.CreatedAt).HasColumnName("created_at");
            client.Property(row => row.LastUsedAt).HasColumnName("last_used_at");
            client.HasIndex(row => row.ClientId).IsUnique();
        });

        modelBuilder.Entity<AccessToken>(token =>
        {
            token.ToTable("access_token");
            token.HasKey(row => row.Id);
            token.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            token.Property(row => row.TenantId).HasColumnName("tenant_id");
            token.Property(row => row.ClientId).HasColumnName("client_id");
            token.Property(row => row.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
            token.Property(row => row.Scopes).HasColumnName("scopes");
            token.Property(row => row.ExpiresAt).HasColumnName("expires_at");
            token.Property(row => row.CreatedAt).HasColumnName("created_at");
            token.HasOne<OAuthClient>().WithMany().HasForeignKey(row => row.ClientId).OnDelete(DeleteBehavior.Cascade);
            token.HasIndex(row => row.TokenHash).IsUnique();
        });

        modelBuilder.Entity<Webhook>(webhook =>
        {
            webhook.ToTable("webhook");
            webhook.HasKey(row => row.Id);
            webhook.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            webhook.Property(row => row.TenantId).HasColumnName("tenant_id");
            webhook.Property(row => row.Url).HasColumnName("url").HasMaxLength(2000);
            webhook.Property(row => row.Subscribed).HasColumnName("events");
            webhook.Property(row => row.Description).HasColumnName("description").HasMaxLength(200);
            webhook.Property(row => row.SecretProtected).HasColumnName("secret_protected");
            webhook.Property(row => row.Active).HasColumnName("active");
            webhook.Property(row => row.PausedAt).HasColumnName("paused_at");
            webhook.Property(row => row.FailingSince).HasColumnName("failing_since");
            webhook.Property(row => row.CreatedAt).HasColumnName("created_at");
            webhook.HasIndex(row => row.TenantId);
        });

        modelBuilder.Entity<WebhookDelivery>(delivery =>
        {
            delivery.ToTable("webhook_delivery");
            delivery.HasKey(row => row.Id);
            delivery.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            delivery.Property(row => row.TenantId).HasColumnName("tenant_id");
            delivery.Property(row => row.WebhookId).HasColumnName("webhook_id");
            delivery.Property(row => row.EventId).HasColumnName("event_id").HasMaxLength(40);
            delivery.Property(row => row.EventType).HasColumnName("event_type").HasMaxLength(40);
            delivery.Property(row => row.Payload).HasColumnName("payload");
            delivery.Property(row => row.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
            delivery.Property(row => row.Attempts).HasColumnName("attempts");
            delivery.Property(row => row.ResponseStatus).HasColumnName("response_status");
            delivery.Property(row => row.Error).HasColumnName("error").HasMaxLength(500);
            delivery.Property(row => row.CreatedAt).HasColumnName("created_at");
            delivery.Property(row => row.DeliveredAt).HasColumnName("delivered_at");
            delivery.HasOne<Webhook>().WithMany().HasForeignKey(row => row.WebhookId).OnDelete(DeleteBehavior.Cascade);
            delivery.HasIndex(row => new { row.WebhookId, row.Id });
        });

        modelBuilder.Entity<TenantSignIn>(signIn =>
        {
            signIn.ToTable("tenant_sign_in");
            signIn.HasKey(row => row.TenantId);
            signIn.Property(row => row.TenantId).HasColumnName("tenant_id").ValueGeneratedNever();
            signIn.Property(row => row.HandoverEnabled).HasColumnName("handover_enabled");
            signIn.Property(row => row.HandoverIssuer).HasColumnName("handover_issuer").HasMaxLength(500);
            signIn.Property(row => row.HandoverKeys).HasColumnName("handover_keys");
            signIn.Property(row => row.HandoverJwksUrl).HasColumnName("handover_jwks_url").HasMaxLength(2000);
            signIn.Property(row => row.ProvisioningEnabled).HasColumnName("provisioning_enabled");
            signIn.Property(row => row.SystemName).HasColumnName("system_name").HasMaxLength(100);
            signIn.Property(row => row.OidcEnabled).HasColumnName("oidc_enabled");
            signIn.Property(row => row.OidcAuthority).HasColumnName("oidc_authority").HasMaxLength(500);
            signIn.Property(row => row.OidcClientId).HasColumnName("oidc_client_id").HasMaxLength(200);
            signIn.Property(row => row.OidcClientSecretProtected).HasColumnName("oidc_client_secret_protected");
            signIn.Property(row => row.OidcDisplayName).HasColumnName("oidc_display_name").HasMaxLength(100);
            signIn.Property(row => row.LdapEnabled).HasColumnName("ldap_enabled");
            signIn.Property(row => row.LdapHost).HasColumnName("ldap_host").HasMaxLength(253);
            signIn.Property(row => row.LdapPort).HasColumnName("ldap_port").HasDefaultValue(636);
            signIn.Property(row => row.LdapBindDn).HasColumnName("ldap_bind_dn").HasMaxLength(500);
            signIn.Property(row => row.LdapBindPasswordProtected).HasColumnName("ldap_bind_password_protected");
            signIn.Property(row => row.LdapBaseDn).HasColumnName("ldap_base_dn").HasMaxLength(500);
            signIn.Property(row => row.LdapLoginAttribute).HasColumnName("ldap_login_attribute").HasMaxLength(64).HasDefaultValue("uid");
            signIn.Property(row => row.LdapObjectClass).HasColumnName("ldap_object_class").HasMaxLength(64).HasDefaultValue("person");
            signIn.Property(row => row.LdapGroupAttribute).HasColumnName("ldap_group_attribute").HasMaxLength(64).HasDefaultValue("memberOf");
            signIn.Property(row => row.LdapCaCertificate).HasColumnName("ldap_ca_certificate");
            signIn.Property(row => row.LdapDisplayName).HasColumnName("ldap_display_name").HasMaxLength(100);
            signIn.Property(row => row.RoleRules).HasColumnName("role_rules").HasColumnType("jsonb")
                .HasConversion(
                    rules => System.Text.Json.JsonSerializer.Serialize(rules, (System.Text.Json.JsonSerializerOptions?)null),
                    json => System.Text.Json.JsonSerializer.Deserialize<List<RoleRule>>(json, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<RoleRule>(),
                    new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlyList<RoleRule>>((left, right) => left!.SequenceEqual(right!), rules => rules.Aggregate(0, (hash, rule) => HashCode.Combine(hash, rule)), rules => rules.ToList()));
            signIn.Property(row => row.DefaultRole).HasColumnName("default_role").HasConversion<string>().HasMaxLength(16);
            signIn.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            signIn.HasOne<Tenant>().WithOne().HasForeignKey<TenantSignIn>(row => row.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalIdentity>(identity =>
        {
            identity.ToTable("external_identity");
            identity.HasKey(row => row.Id);
            identity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            identity.Property(row => row.TenantId).HasColumnName("tenant_id");
            identity.Property(row => row.UserId).HasColumnName("user_id");
            identity.Property(row => row.ExternalId).HasColumnName("external_id").HasMaxLength(200);
            identity.Property(row => row.Source).HasColumnName("source").HasMaxLength(20);
            identity.Property(row => row.Active).HasColumnName("active");
            identity.Property(row => row.CreatedAt).HasColumnName("created_at");
            identity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            identity.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Cascade);
            identity.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
            identity.HasIndex(row => new { row.TenantId, row.ExternalId }).IsUnique();
            identity.HasIndex(row => new { row.TenantId, row.UserId }).IsUnique();
        });

        modelBuilder.Entity<EmbedSession>(session =>
        {
            session.ToTable("embed_session");
            session.HasKey(row => row.Id);
            session.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            session.Property(row => row.TenantId).HasColumnName("tenant_id");
            session.Property(row => row.UserId).HasColumnName("user_id");
            session.Property(row => row.SurveyId).HasColumnName("survey_id");
            session.Property(row => row.View).HasColumnName("view").HasMaxLength(20);
            session.Property(row => row.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
            session.Property(row => row.ExpiresAt).HasColumnName("expires_at");
            session.Property(row => row.EndsAt).HasColumnName("ends_at");
            session.Property(row => row.CreatedAt).HasColumnName("created_at");
            session.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Cascade);
            session.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
            session.HasIndex(row => row.TokenHash).IsUnique();
        });

        modelBuilder.Entity<HandoverTokenUse>(use =>
        {
            use.ToTable("handover_token_use");
            use.HasKey(row => new { row.TenantId, row.Jti });
            use.Property(row => row.TenantId).HasColumnName("tenant_id");
            use.Property(row => row.Jti).HasColumnName("jti").HasMaxLength(200);
            use.Property(row => row.ExpiresAt).HasColumnName("expires_at");
        });

        modelBuilder.Entity<MemberInvitation>(invitation =>
        {
            invitation.ToTable("member_invitation");
            invitation.HasKey(row => row.Id);
            invitation.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            invitation.Property(row => row.TenantId).HasColumnName("tenant_id");
            invitation.Property(row => row.Email).HasColumnName("email").HasMaxLength(254);
            invitation.Property(row => row.Role).HasColumnName("role");
            invitation.Property(row => row.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
            invitation.Property(row => row.TokenProtected).HasColumnName("token_protected");
            invitation.Property(row => row.InvitedBy).HasColumnName("invited_by");
            invitation.Property(row => row.InvitedAt).HasColumnName("invited_at");
            invitation.Property(row => row.ExpiresAt).HasColumnName("expires_at");
            invitation.Property(row => row.SentAt).HasColumnName("sent_at");
            invitation.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Cascade);
            invitation.HasIndex(row => row.TokenHash).IsUnique();
            invitation.HasIndex(row => new { row.TenantId, row.Email }).IsUnique();
        });

        modelBuilder.Entity<Membership>(membership =>
        {
            membership.ToTable("membership");
            membership.HasKey(row => row.Id);
            membership.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            membership.Property(row => row.UserId).HasColumnName("user_id");
            membership.Property(row => row.TenantId).HasColumnName("tenant_id");
            membership.Property(row => row.WorkspaceId).HasColumnName("workspace_id");
            membership.Property(row => row.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(16);
            membership.Property(row => row.CreatedAt).HasColumnName("created_at");
            membership.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
            membership.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Cascade);
            membership.HasIndex(row => new { row.UserId, row.TenantId, row.WorkspaceId }).IsUnique().AreNullsDistinct(false);
            membership.HasIndex(row => row.TenantId);
        });

        modelBuilder.Entity<AuditLogRow>(audit =>
        {
            audit.ToTable("audit_log");
            audit.HasKey(row => row.Id);
            audit.Property(row => row.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            audit.Property(row => row.TenantId).HasColumnName("tenant_id");
            audit.Property(row => row.Sequence).HasColumnName("sequence");
            audit.Property(row => row.OccurredAt).HasColumnName("occurred_at");
            audit.Property(row => row.ActorKind).HasColumnName("actor_kind").HasMaxLength(32);
            audit.Property(row => row.ActorId).HasColumnName("actor_id").HasMaxLength(200);
            audit.Property(row => row.Action).HasColumnName("action").HasMaxLength(200);
            audit.Property(row => row.EntityType).HasColumnName("entity_type").HasMaxLength(100);
            audit.Property(row => row.EntityId).HasColumnName("entity_id").HasMaxLength(200);

            // Text, not jsonb: jsonb normalises the JSON, which would change the hashed content.
            audit.Property(row => row.Diff).HasColumnName("diff");
            audit.Property(row => row.TraceId).HasColumnName("trace_id").HasMaxLength(32);
            audit.Property(row => row.PreviousHash).HasColumnName("previous_hash").HasMaxLength(64);
            audit.Property(row => row.Hash).HasColumnName("hash").HasMaxLength(64);
            audit.HasIndex(row => new { row.TenantId, row.Sequence }).IsUnique().AreNullsDistinct(false);
        });

        modelBuilder.Entity<Survey>(survey =>
        {
            survey.ToTable("survey");
            survey.HasKey(row => row.Id);
            survey.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            survey.Property(row => row.TenantId).HasColumnName("tenant_id");
            survey.Property<Guid?>("WorkspaceId").HasColumnName("workspace_id");
            survey.Property(row => row.EmbedOrigins).HasColumnName("embed_origins").HasDefaultValueSql("'{}'");
            survey.Property(row => row.Title).HasColumnName("title").HasMaxLength(200);
            survey.Property(row => row.Code).HasColumnName("code").HasMaxLength(16);
            survey.HasIndex(row => row.Code).IsUnique();
            survey.Property(row => row.Status).HasColumnName("status").HasConversion(LowerCase<SurveyStatus>()).HasMaxLength(16);
            survey.Property(row => row.PublishedVersion).HasColumnName("published_version");
            survey.Property(row => row.Languages).HasColumnName("languages");
            survey.Property(row => row.DefaultLanguage).HasColumnName("default_language").HasMaxLength(16);
            survey.Property<string?>("AnonymityMode").HasColumnName("anonymity_mode").HasMaxLength(32);
            survey.Property<string>("Settings").HasColumnName("settings").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            survey.Property(row => row.CreatedBy).HasColumnName("created_by");
            survey.Property(row => row.CreatedAt).HasColumnName("created_at");
            survey.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            survey.Property(row => row.DeletedAt).HasColumnName("deleted_at");
            survey.Property(row => row.ThemeId).HasColumnName("theme_id");
            survey.Property(row => row.Look).HasColumnName("look").HasColumnType("jsonb");
            survey.Property(row => row.LookBase).HasColumnName("look_base").HasMaxLength(80);
            survey.Property(row => row.ClosesAt).HasColumnName("closes_at");
            survey.Property(row => row.OneResponsePerPerson).HasColumnName("one_response_per_person");
            survey.Property(row => row.AutoReminders).HasColumnName("auto_reminders");
            survey.Property(row => row.ReminderDays).HasColumnName("reminder_days").HasDefaultValue(7);
            survey.Property(row => row.ReminderMax).HasColumnName("reminder_max").HasDefaultValue(2);
            survey.Property(row => row.ReminderRevision).HasColumnName("reminder_revision");
            survey.HasOne<Theme>().WithMany().HasForeignKey(row => row.ThemeId).OnDelete(DeleteBehavior.SetNull);
            survey.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
            survey.HasOne<User>().WithMany().HasForeignKey(row => row.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            survey.HasIndex(row => new { row.TenantId, row.UpdatedAt });
        });

        modelBuilder.Entity<SurveyVersion>(version =>
        {
            version.ToTable("survey_version");
            version.HasKey(row => row.Id);
            version.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            version.Property(row => row.TenantId).HasColumnName("tenant_id");
            version.Property(row => row.SurveyId).HasColumnName("survey_id");
            version.Property(row => row.Number).HasColumnName("number");
            version.Property(row => row.Definition).HasColumnName("definition").HasColumnType("jsonb");
            version.Property(row => row.State).HasColumnName("state").HasConversion(LowerCase<SurveyVersionState>()).HasMaxLength(16);
            version.Property(row => row.PublishedAt).HasColumnName("published_at");
            version.Property(row => row.CreatedBy).HasColumnName("created_by");
            version.Property(row => row.CreatedAt).HasColumnName("created_at");
            version.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            version.HasOne<Survey>().WithMany().HasForeignKey(row => row.SurveyId).OnDelete(DeleteBehavior.Restrict);
            version.HasOne<User>().WithMany().HasForeignKey(row => row.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            version.HasIndex(row => new { row.SurveyId, row.Number }).IsUnique();

            // Exactly one draft per survey (data-model.md).
            version.HasIndex(row => row.SurveyId).IsUnique().HasFilter("state = 'draft'").HasDatabaseName("ix_survey_version_one_draft");
        });

        modelBuilder.Entity<SurveyPageIndex>(page =>
        {
            page.ToTable("survey_page");
            page.HasKey(row => row.Id);
            page.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            page.Property(row => row.TenantId).HasColumnName("tenant_id");
            page.Property(row => row.SurveyVersionId).HasColumnName("survey_version_id");
            page.Property(row => row.PageKey).HasColumnName("page_key").HasMaxLength(100);
            page.Property(row => row.Position).HasColumnName("position");
            page.Property(row => row.Title).HasColumnName("title").HasColumnType("jsonb");
            page.HasOne<SurveyVersion>().WithMany().HasForeignKey(row => row.SurveyVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SurveyQuestionIndex>(question =>
        {
            question.ToTable("survey_question");
            question.HasKey(row => row.Id);
            question.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            question.Property(row => row.TenantId).HasColumnName("tenant_id");
            question.Property(row => row.SurveyVersionId).HasColumnName("survey_version_id");
            question.Property(row => row.PageId).HasColumnName("page_id");
            question.Property(row => row.QuestionKey).HasColumnName("question_key").HasMaxLength(100);
            question.Property(row => row.Position).HasColumnName("position");
            question.Property(row => row.VariableName).HasColumnName("variable_name").HasMaxLength(64);
            question.Property(row => row.Type).HasColumnName("type").HasMaxLength(32);
            question.Property(row => row.Text).HasColumnName("text").HasColumnType("jsonb");
            question.Property(row => row.Required).HasColumnName("required");
            question.HasOne<SurveyVersion>().WithMany().HasForeignKey(row => row.SurveyVersionId).OnDelete(DeleteBehavior.Restrict);
            question.HasOne<SurveyPageIndex>().WithMany().HasForeignKey(row => row.PageId).OnDelete(DeleteBehavior.Restrict);
            question.HasIndex(row => new { row.SurveyVersionId, row.VariableName }).IsUnique();
        });

        modelBuilder.Entity<SurveyOptionIndex>(option =>
        {
            option.ToTable("survey_option");
            option.HasKey(row => row.Id);
            option.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            option.Property(row => row.TenantId).HasColumnName("tenant_id");
            option.Property(row => row.QuestionId).HasColumnName("question_id");
            option.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(8);
            option.Property(row => row.Position).HasColumnName("position");
            option.Property(row => row.Code).HasColumnName("code").HasMaxLength(64);
            option.Property(row => row.Label).HasColumnName("label").HasColumnType("jsonb");
            option.HasOne<SurveyQuestionIndex>().WithMany().HasForeignKey(row => row.QuestionId).OnDelete(DeleteBehavior.Restrict);
            option.HasIndex(row => new { row.QuestionId, row.Kind, row.Code }).IsUnique();
        });

        modelBuilder.Entity<VariableMapping>(mapping =>
        {
            mapping.ToTable("variable_mapping");
            mapping.HasKey(row => row.Id);
            mapping.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            mapping.Property(row => row.TenantId).HasColumnName("tenant_id");
            mapping.Property(row => row.SurveyId).HasColumnName("survey_id");
            mapping.Property(row => row.FromName).HasColumnName("from_name").HasMaxLength(64);
            mapping.Property(row => row.ToName).HasColumnName("to_name").HasMaxLength(64);
            mapping.Property(row => row.SinceVersion).HasColumnName("since_version");
            mapping.HasOne<Survey>().WithMany().HasForeignKey(row => row.SurveyId).OnDelete(DeleteBehavior.Restrict);
            mapping.HasIndex(row => row.SurveyId);
        });

        modelBuilder.Entity<SurveyApproval>(approval =>
        {
            approval.ToTable("survey_approval");
            approval.HasKey(row => row.Id);
            approval.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            approval.Property(row => row.TenantId).HasColumnName("tenant_id");
            approval.Property(row => row.SurveyId).HasColumnName("survey_id");
            approval.Property(row => row.Version).HasColumnName("version");
            approval.Property(row => row.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64);
            approval.Property(row => row.Note).HasColumnName("note").HasMaxLength(500);
            approval.Property(row => row.ApprovedBy).HasColumnName("approved_by");
            approval.Property(row => row.ApprovedAt).HasColumnName("approved_at");
            approval.HasOne<Survey>().WithMany().HasForeignKey(row => row.SurveyId).OnDelete(DeleteBehavior.Restrict);
            approval.HasIndex(row => row.SurveyId);
        });

        modelBuilder.Entity<Theme>(theme =>
        {
            theme.ToTable("theme");
            theme.HasKey(row => row.Id);
            theme.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            theme.Property(row => row.TenantId).HasColumnName("tenant_id");
            theme.Property(row => row.Name).HasColumnName("name").HasMaxLength(100);
            theme.Property(row => row.Tokens).HasColumnName("tokens").HasColumnType("jsonb");
            theme.Property(row => row.IsDefault).HasColumnName("is_default");
            theme.Property(row => row.CreatedBy).HasColumnName("created_by");
            theme.Property(row => row.CreatedAt).HasColumnName("created_at");
            theme.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            theme.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
            theme.HasOne<User>().WithMany().HasForeignKey(row => row.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            // One default per organisation.
            theme.HasIndex(row => row.TenantId).HasFilter("is_default").IsUnique().HasDatabaseName("IX_theme_tenant_id_default");
        });

        modelBuilder.Entity<MessageTemplate>(message =>
        {
            var texts = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<TranslatedText, string>(
                text => System.Text.Json.JsonSerializer.Serialize(text, SurveyDefinitionJson.Options),
                json => System.Text.Json.JsonSerializer.Deserialize<TranslatedText>(json, SurveyDefinitionJson.Options)!);
            message.ToTable("message_template");
            message.HasKey(row => row.Id);
            message.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            message.Property(row => row.TenantId).HasColumnName("tenant_id");
            message.Property(row => row.Kind).HasColumnName("kind").HasConversion(LowerCase<MessageKind>()).HasMaxLength(16);
            message.Property(row => row.Subject).HasColumnName("subject").HasColumnType("jsonb").HasConversion(texts);
            message.Property(row => row.Body).HasColumnName("body").HasColumnType("jsonb").HasConversion(texts);
            message.Property(row => row.AnswerInEmail).HasColumnName("answer_in_email");
            message.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            message.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
            message.HasIndex(row => new { row.TenantId, row.Kind }).IsUnique();
        });

        modelBuilder.Entity<SurveyTemplate>(template =>
        {
            template.ToTable("survey_template");
            template.HasKey(row => row.Id);
            template.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            template.Property(row => row.TenantId).HasColumnName("tenant_id");
            template.Property(row => row.Name).HasColumnName("name").HasMaxLength(200);
            template.Property(row => row.Definition).HasColumnName("definition").HasColumnType("jsonb");
            template.Property(row => row.CreatedBy).HasColumnName("created_by");
            template.Property(row => row.CreatedAt).HasColumnName("created_at");
            template.Property(row => row.SurveyId).HasColumnName("survey_id");
            template.HasOne<Survey>().WithMany().HasForeignKey(row => row.SurveyId).OnDelete(DeleteBehavior.SetNull);
            template.HasIndex(row => row.SurveyId).IsUnique();
            template.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
            template.HasOne<User>().WithMany().HasForeignKey(row => row.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            template.HasIndex(row => row.TenantId);
        });

        modelBuilder.Entity<Asset>(asset =>
        {
            asset.ToTable("asset");
            asset.HasKey(row => row.Id);
            asset.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            asset.Property(row => row.TenantId).HasColumnName("tenant_id");
            asset.Property(row => row.Kind).HasColumnName("kind").HasConversion(LowerCase<AssetKind>()).HasMaxLength(16);
            asset.Property(row => row.ContentType).HasColumnName("content_type").HasMaxLength(64);
            asset.Property(row => row.Size).HasColumnName("size");
            asset.Property(row => row.Sha256).HasColumnName("sha256").HasMaxLength(64);
            asset.Property(row => row.OriginalName).HasColumnName("original_name").HasMaxLength(200);
            asset.Property(row => row.StorageKey).HasColumnName("storage_key").HasMaxLength(100);
            asset.Property(row => row.Width).HasColumnName("width");
            asset.Property(row => row.Height).HasColumnName("height");
            asset.Property(row => row.CreatedBy).HasColumnName("created_by");
            asset.Property(row => row.CreatedAt).HasColumnName("created_at");
            asset.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
            asset.HasOne<User>().WithMany().HasForeignKey(row => row.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            asset.HasIndex(row => row.TenantId);
        });

        modelBuilder.Entity<Askwerk.Domain.Domains.CustomDomain>(domain =>
        {
            domain.ToTable("domain");
            domain.HasKey(row => row.Id);
            domain.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            domain.Property(row => row.TenantId).HasColumnName("tenant_id");
            domain.Property(row => row.Hostname).HasColumnName("hostname").HasMaxLength(253);
            domain.Property(row => row.VerificationToken).HasColumnName("verification_token").HasMaxLength(64);
            domain.Property(row => row.Status).HasColumnName("status").HasConversion(LowerCase<Askwerk.Domain.Domains.DomainStatus>()).HasMaxLength(16);
            domain.Property(row => row.IsPrimary).HasColumnName("is_primary");
            domain.Property(row => row.CnameFound).HasColumnName("cname_found");
            domain.Property(row => row.TxtFound).HasColumnName("txt_found");
            domain.Property(row => row.CreatedAt).HasColumnName("created_at");
            domain.Property(row => row.CheckedAt).HasColumnName("checked_at");
            domain.Property(row => row.VerifiedAt).HasColumnName("verified_at");
            domain.Ignore(row => row.TxtName);
            domain.Ignore(row => row.TxtValue);
            domain.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);

            // One organisation per hostname, across the instance; at most one primary domain per organisation.
            domain.HasIndex(row => row.Hostname).IsUnique();
            domain.HasIndex(row => row.TenantId).IsUnique().HasFilter("is_primary").HasDatabaseName("ix_domain_one_primary");
        });

        modelBuilder.Entity<Askwerk.Domain.Participants.Invitation>(invitation =>
        {
            invitation.ToTable("invitation");
            invitation.HasKey(row => row.Id);
            invitation.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            invitation.Property(row => row.TenantId).HasColumnName("tenant_id");
            invitation.Property(row => row.SurveyId).HasColumnName("survey_id");
            invitation.Property(row => row.Send).HasColumnName("send");
            invitation.Property(row => row.SendAt).HasColumnName("send_at");
            invitation.Property(row => row.Deadline).HasColumnName("deadline");
            invitation.Property(row => row.RemindAt).HasColumnName("remind_at");
            invitation.Property(row => row.Host).HasColumnName("host").HasMaxLength(253);
            invitation.Property(row => row.CreatedBy).HasColumnName("created_by");
            invitation.Property(row => row.CreatedAt).HasColumnName("created_at");
            invitation.HasOne<Survey>().WithMany().HasForeignKey(row => row.SurveyId).OnDelete(DeleteBehavior.Restrict);
            invitation.HasIndex(row => row.SurveyId);
        });

        modelBuilder.Entity<Askwerk.Domain.Participants.Participant>(participant =>
        {
            participant.ToTable("participant");
            participant.HasKey(row => row.Id);
            participant.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            participant.Property(row => row.TenantId).HasColumnName("tenant_id");
            participant.Property(row => row.SurveyId).HasColumnName("survey_id");
            participant.Property(row => row.InvitationId).HasColumnName("invitation_id");
            participant.Property(row => row.Email).HasColumnName("email").HasMaxLength(254);
            participant.Property(row => row.Name).HasColumnName("name").HasMaxLength(200);
            participant.Property(row => row.Language).HasColumnName("language").HasMaxLength(16);
            participant.Property(row => row.Token).HasColumnName("token").HasMaxLength(64);
            participant.Property(row => row.CreatedAt).HasColumnName("created_at");
            participant.Property(row => row.SentAt).HasColumnName("sent_at");
            participant.Property(row => row.OpenedAt).HasColumnName("opened_at");
            participant.Property(row => row.BouncedAt).HasColumnName("bounced_at");
            participant.Property(row => row.Reminders).HasColumnName("reminders");
            participant.Property(row => row.RemindedAt).HasColumnName("reminded_at");
            participant.Property(row => row.ExternalId).HasColumnName("external_id").HasMaxLength(200);
            participant.Property(row => row.Fields).HasColumnName("fields").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            participant.Property(row => row.ContactId).HasColumnName("contact_id");
            participant.Property(row => row.RemindersOff).HasColumnName("reminders_off");
            participant.HasOne<Askwerk.Domain.Participants.Contact>().WithMany().HasForeignKey(row => row.ContactId).OnDelete(DeleteBehavior.SetNull);
            participant.HasIndex(row => new { row.SurveyId, row.ExternalId });
            participant.HasOne<Survey>().WithMany().HasForeignKey(row => row.SurveyId).OnDelete(DeleteBehavior.Restrict);
            participant.HasOne<Askwerk.Domain.Participants.Invitation>().WithMany().HasForeignKey(row => row.InvitationId).OnDelete(DeleteBehavior.Restrict);
            participant.HasIndex(row => row.Token).IsUnique();
            participant.HasIndex(row => new { row.SurveyId, row.Email }).IsUnique();
        });

        modelBuilder.Entity<Askwerk.Domain.Participants.Contact>(contact =>
        {
            contact.ToTable("contact");
            contact.HasKey(row => row.Id);
            contact.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            contact.Property(row => row.TenantId).HasColumnName("tenant_id");
            contact.Property(row => row.Email).HasColumnName("email").HasMaxLength(254);
            contact.Property(row => row.Name).HasColumnName("name").HasMaxLength(200);
            contact.Property(row => row.Language).HasColumnName("language").HasMaxLength(16);
            contact.Property(row => row.ExternalId).HasColumnName("external_id").HasMaxLength(200);
            contact.Property(row => row.Fields).HasColumnName("fields").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            contact.Property(row => row.Groups).HasColumnName("groups").HasDefaultValueSql("'{}'");
            contact.Property(row => row.CreatedAt).HasColumnName("created_at");
            contact.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            contact.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
            contact.HasIndex(row => new { row.TenantId, row.Email }).IsUnique();
            contact.HasIndex(row => new { row.TenantId, row.ExternalId }).IsUnique().HasFilter("external_id IS NOT NULL");
        });

        modelBuilder.Entity<Askwerk.Domain.Participants.Unsubscribe>(unsubscribe =>
        {
            unsubscribe.ToTable("unsubscribe");
            unsubscribe.HasKey(row => row.Id);
            unsubscribe.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            unsubscribe.Property(row => row.TenantId).HasColumnName("tenant_id");
            unsubscribe.Property(row => row.Email).HasColumnName("email").HasMaxLength(254);
            unsubscribe.Property(row => row.CreatedAt).HasColumnName("created_at");
            unsubscribe.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
            unsubscribe.HasIndex(row => new { row.TenantId, row.Email }).IsUnique();
        });

        modelBuilder.Entity<Askwerk.Domain.Integrations.ApiKey>(key =>
        {
            key.ToTable("api_key");
            key.HasKey(row => row.Id);
            key.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            key.Property(row => row.TenantId).HasColumnName("tenant_id");
            key.Property(row => row.Name).HasColumnName("name").HasMaxLength(100);
            key.Property(row => row.Prefix).HasColumnName("prefix").HasMaxLength(12);
            key.Property(row => row.SecretHash).HasColumnName("secret_hash").HasMaxLength(64);
            key.Property(row => row.Scopes).HasColumnName("scopes");
            key.Property(row => row.ExpiresAt).HasColumnName("expires_at");
            key.Property(row => row.RevokedAt).HasColumnName("revoked_at");
            key.Property(row => row.CreatedBy).HasColumnName("created_by");
            key.Property(row => row.CreatedAt).HasColumnName("created_at");
            key.Property(row => row.LastUsedAt).HasColumnName("last_used_at");
            key.Ignore(row => row.Shown);
            key.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
            key.HasIndex(row => row.Prefix).IsUnique();
            key.HasIndex(row => row.TenantId);
        });

        modelBuilder.Entity<Askwerk.Domain.Integrations.DataExport>(export =>
        {
            export.ToTable("export");
            export.HasKey(row => row.Id);
            export.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            export.Property(row => row.TenantId).HasColumnName("tenant_id");
            export.Property(row => row.SurveyId).HasColumnName("survey_id");
            export.Property(row => row.Format).HasColumnName("format").HasMaxLength(16);
            export.Property(row => row.Options).HasColumnName("options").HasColumnType("jsonb");
            export.Property(row => row.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
            export.Property(row => row.RequestedByUserId).HasColumnName("requested_by_user_id");
            export.Property(row => row.RequestedByApiKeyId).HasColumnName("requested_by_api_key_id");
            export.Property(row => row.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
            export.Property(row => row.JobId).HasColumnName("job_id");
            export.Property(row => row.Done).HasColumnName("done");
            export.Property(row => row.Total).HasColumnName("total");
            export.Property(row => row.FileKey).HasColumnName("file_key").HasMaxLength(300);
            export.Property(row => row.FileName).HasColumnName("file_name").HasMaxLength(200);
            export.Property(row => row.ContentType).HasColumnName("content_type").HasMaxLength(100);
            export.Property(row => row.Size).HasColumnName("size");
            export.Property(row => row.Error).HasColumnName("error").HasMaxLength(2000);
            export.Property(row => row.CreatedAt).HasColumnName("created_at");
            export.Property(row => row.FinishedAt).HasColumnName("finished_at");
            export.Property(row => row.ExpiresAt).HasColumnName("expires_at");
            export.HasOne<Survey>().WithMany().HasForeignKey(row => row.SurveyId).OnDelete(DeleteBehavior.Restrict);
            export.HasOne<Askwerk.Domain.Integrations.ApiKey>().WithMany().HasForeignKey(row => row.RequestedByApiKeyId).OnDelete(DeleteBehavior.Restrict);
            export.HasIndex(row => new { row.SurveyId, row.CreatedAt });
            export.HasIndex(row => new { row.RequestedByApiKeyId, row.IdempotencyKey });
        });

        modelBuilder.Entity<Askwerk.Domain.Jobs.Job>(job =>
        {
            job.ToTable("job");
            job.HasKey(row => row.Id);
            job.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            job.Property(row => row.TenantId).HasColumnName("tenant_id");
            job.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(64);
            job.Property(row => row.Payload).HasColumnName("payload").HasColumnType("jsonb");
            job.Property(row => row.RunAt).HasColumnName("run_at");
            job.Property(row => row.Attempts).HasColumnName("attempts");
            job.Property(row => row.LockedUntil).HasColumnName("locked_until");
            job.Property(row => row.DoneAt).HasColumnName("done_at");
            job.Property(row => row.Error).HasColumnName("error").HasMaxLength(2000);
            job.Property(row => row.CreatedAt).HasColumnName("created_at");
            job.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);

            // The workers look for due jobs that are not done.
            job.HasIndex(row => row.RunAt).HasFilter("done_at IS NULL").HasDatabaseName("ix_job_due");
        });

        modelBuilder.Entity<SurveyResponse>(response =>
        {
            response.ToTable("response");
            response.Property(row => row.RespondentExternalId).HasColumnName("respondent_external_id").HasMaxLength(200);
            response.Property(row => row.RespondentEmail).HasColumnName("respondent_email").HasMaxLength(320);
            response.HasKey(row => row.Id);
            response.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            response.Property(row => row.TenantId).HasColumnName("tenant_id");
            response.Property(row => row.SurveyId).HasColumnName("survey_id");
            response.Property(row => row.SurveyVersionId).HasColumnName("survey_version_id");
            response.Property<Guid?>("CollectorId").HasColumnName("collector_id");
            response.Property(row => row.ParticipantId).HasColumnName("participant_id");
            response.Property(row => row.Status).HasColumnName("status").HasConversion(LowerCase<ResponseStatus>()).HasMaxLength(16);
            response.Property(row => row.Language).HasColumnName("language").HasMaxLength(16);
            response.Property(row => row.PageKey).HasColumnName("page_key").HasMaxLength(100);
            response.Property(row => row.ResumeTokenHash).HasColumnName("resume_token_hash").HasMaxLength(64);
            response.Property(row => row.StartedAt).HasColumnName("started_at");
            response.Property(row => row.CompletedAt).HasColumnName("completed_at");
            response.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            response.Property(row => row.ChangeSeq).HasColumnName("change_seq").ValueGeneratedOnAddOrUpdate().HasDefaultValueSql("nextval('response_change_seq')");
            response.HasOne<Survey>().WithMany().HasForeignKey(row => row.SurveyId).OnDelete(DeleteBehavior.Restrict);
            response.HasOne<SurveyVersion>().WithMany().HasForeignKey(row => row.SurveyVersionId).OnDelete(DeleteBehavior.Restrict);
            response.HasOne<Askwerk.Domain.Participants.Participant>().WithMany().HasForeignKey(row => row.ParticipantId).OnDelete(DeleteBehavior.Restrict);
            response.HasIndex(row => row.ResumeTokenHash).IsUnique();
            response.HasIndex(row => new { row.SurveyId, row.ChangeSeq });

            // The response list: newest first, on a cursor (AWK-24).
            response.HasIndex(row => new { row.SurveyId, row.StartedAt, row.Id });
        });

        modelBuilder.Entity<Answer>(answer =>
        {
            answer.ToTable("answer");
            answer.HasKey(row => row.Id);
            answer.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            answer.Property(row => row.TenantId).HasColumnName("tenant_id");
            answer.Property(row => row.ResponseId).HasColumnName("response_id");
            answer.Property(row => row.QuestionId).HasColumnName("question_id");
            answer.Property(row => row.NumberValue).HasColumnName("number_value");
            answer.Property(row => row.TextValue).HasColumnName("text_value");
            answer.Property(row => row.DateValue).HasColumnName("date_value").HasMaxLength(16);
            answer.Property(row => row.OptionCodes).HasColumnName("option_codes");
            answer.Property(row => row.Detail).HasColumnName("detail").HasColumnType("jsonb");
            answer.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            answer.HasOne<SurveyResponse>().WithMany().HasForeignKey(row => row.ResponseId).OnDelete(DeleteBehavior.Cascade);
            answer.HasOne<SurveyQuestionIndex>().WithMany().HasForeignKey(row => row.QuestionId).OnDelete(DeleteBehavior.Restrict);
            answer.HasIndex(row => new { row.ResponseId, row.QuestionId }).IsUnique();
        });
    }

    /// <summary>Stores an enum as its lower-case name, as the data model writes the values.</summary>
    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<TEnum, string> LowerCase<TEnum>()
        where TEnum : struct, Enum =>
        new(value => value.ToString().ToLowerInvariant(), text => Enum.Parse<TEnum>(text, true));
}
