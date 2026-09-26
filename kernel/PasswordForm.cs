using System;
using System.Collections.Generic;

namespace DbChange.Kernel;

/// <summary>
/// A form of T-SQL that sets a password, a key or a secret with a literal, as the T-SQL reference writes it: what the printer
/// (io/SchemaText.cs) looks for in schema text before it leaves the tool, so the value a form sets is printed as left out and the
/// object that sets it gets a schema.password-literal warning (decision 2.27, VALUES.md X2). The set is closed, one member per form,
/// so a form without a planted sample fails the printer's test; a form the list lacks is printed as written until it is added here.
/// BACKUP … WITH PASSWORD or MEDIAPASSWORD is no member: SQL Server 2012 discontinued both, the 2022 grammar the printer parses with
/// reads neither, and text holding them is left out whole as unparsed.
/// </summary>
public sealed record PasswordForm : IComparable<PasswordForm>
{
    public static readonly PasswordForm CreateLogin = new("CreateLogin", "CREATE LOGIN … WITH PASSWORD");
    public static readonly PasswordForm AlterLogin = new("AlterLogin", "ALTER LOGIN … WITH PASSWORD (or OLD_PASSWORD)");
    public static readonly PasswordForm CreateUser = new("CreateUser", "CREATE USER … WITH PASSWORD");
    public static readonly PasswordForm AlterUser = new("AlterUser", "ALTER USER … WITH PASSWORD (or OLD_PASSWORD)");
    public static readonly PasswordForm CreateApplicationRole = new("CreateApplicationRole", "CREATE APPLICATION ROLE … WITH PASSWORD");
    public static readonly PasswordForm AlterApplicationRole = new("AlterApplicationRole", "ALTER APPLICATION ROLE … WITH PASSWORD");
    public static readonly PasswordForm CreateMasterKey = new("CreateMasterKey", "CREATE MASTER KEY ENCRYPTION BY PASSWORD");
    public static readonly PasswordForm AlterMasterKey = new("AlterMasterKey", "ALTER MASTER KEY … BY PASSWORD");
    public static readonly PasswordForm OpenMasterKey = new("OpenMasterKey", "OPEN MASTER KEY DECRYPTION BY PASSWORD");
    public static readonly PasswordForm BackupMasterKey = new("BackupMasterKey", "BACKUP MASTER KEY … ENCRYPTION BY PASSWORD");
    public static readonly PasswordForm RestoreMasterKey = new("RestoreMasterKey", "RESTORE MASTER KEY … DECRYPTION BY PASSWORD … ENCRYPTION BY PASSWORD");
    public static readonly PasswordForm CreateAsymmetricKey = new("CreateAsymmetricKey", "CREATE ASYMMETRIC KEY … ENCRYPTION BY PASSWORD");
    public static readonly PasswordForm AlterAsymmetricKey = new("AlterAsymmetricKey", "ALTER ASYMMETRIC KEY … BY PASSWORD");
    public static readonly PasswordForm CreateCertificate = new("CreateCertificate", "CREATE CERTIFICATE … ENCRYPTION BY PASSWORD (or DECRYPTION BY PASSWORD)");
    public static readonly PasswordForm AlterCertificate = new("AlterCertificate", "ALTER CERTIFICATE … BY PASSWORD");
    public static readonly PasswordForm BackupCertificate = new("BackupCertificate", "BACKUP CERTIFICATE … BY PASSWORD");
    public static readonly PasswordForm CreateSymmetricKey = new("CreateSymmetricKey", "CREATE SYMMETRIC KEY … KEY_SOURCE, IDENTITY_VALUE or ENCRYPTION BY PASSWORD");
    public static readonly PasswordForm AlterSymmetricKey = new("AlterSymmetricKey", "ALTER SYMMETRIC KEY … BY PASSWORD");
    public static readonly PasswordForm OpenSymmetricKey = new("OpenSymmetricKey", "OPEN SYMMETRIC KEY … DECRYPTION BY PASSWORD");
    public static readonly PasswordForm AddSignature = new("AddSignature", "ADD SIGNATURE … WITH PASSWORD");
    public static readonly PasswordForm AddCounterSignature = new("AddCounterSignature", "ADD COUNTER SIGNATURE … WITH PASSWORD");
    public static readonly PasswordForm CreateCredential = new("CreateCredential", "CREATE CREDENTIAL … SECRET");
    public static readonly PasswordForm AlterCredential = new("AlterCredential", "ALTER CREDENTIAL … SECRET");
    public static readonly PasswordForm CreateDatabaseScopedCredential = new("CreateDatabaseScopedCredential", "CREATE DATABASE SCOPED CREDENTIAL … SECRET");
    public static readonly PasswordForm AlterDatabaseScopedCredential = new("AlterDatabaseScopedCredential", "ALTER DATABASE SCOPED CREDENTIAL … SECRET");
    public static readonly PasswordForm CreateExternalDataSource = new("CreateExternalDataSource", "CREATE EXTERNAL DATA SOURCE … CONNECTION_OPTIONS");
    public static readonly PasswordForm Restore = new("Restore", "RESTORE … WITH PASSWORD or MEDIAPASSWORD");
    public static readonly PasswordForm OpenRowset = new("OpenRowset", "OPENROWSET (… a password or a provider string …)");
    public static readonly PasswordForm OpenDataSource = new("OpenDataSource", "OPENDATASOURCE (…, an init string)");
    public static readonly PasswordForm SpPassword = new("SpPassword", "sp_password");
    public static readonly PasswordForm SpAddLogin = new("SpAddLogin", "sp_addlogin @passwd");
    public static readonly PasswordForm SpAppRolePassword = new("SpAppRolePassword", "sp_approlepassword @newpwd");
    public static readonly PasswordForm SpAddAppRole = new("SpAddAppRole", "sp_addapprole @password");
    public static readonly PasswordForm SpSetAppRole = new("SpSetAppRole", "sp_setapprole @password");
    public static readonly PasswordForm SpAddLinkedSrvLogin = new("SpAddLinkedSrvLogin", "sp_addlinkedsrvlogin @rmtpassword");
    public static readonly PasswordForm SpAddLinkedServer = new("SpAddLinkedServer", "sp_addlinkedserver @provstr");
    public static readonly PasswordForm SpControlDbMasterKeyPassword = new("SpControlDbMasterKeyPassword", "sp_control_dbmasterkey_password @password");
    public static readonly PasswordForm VariableOrParameter = new("VariableOrParameter", "a variable or parameter named for a password, a pwd or a secret, given a literal");
    public static readonly PasswordForm SqlCmdConnect = new("SqlCmdConnect", ":connect … -P");

    /// <summary>Every form, in name order: the theory the printer's test runs one planted sample per.</summary>
    public static readonly IReadOnlyList<PasswordForm> All =
    [
        AddCounterSignature, AddSignature, AlterApplicationRole, AlterAsymmetricKey, AlterCertificate, AlterCredential, AlterDatabaseScopedCredential, AlterLogin,
        AlterMasterKey, AlterSymmetricKey, AlterUser, BackupCertificate, BackupMasterKey, CreateApplicationRole, CreateAsymmetricKey, CreateCertificate,
        CreateCredential, CreateDatabaseScopedCredential, CreateExternalDataSource, CreateLogin, CreateMasterKey, CreateSymmetricKey, CreateUser, OpenDataSource,
        OpenMasterKey, OpenRowset, OpenSymmetricKey, Restore, RestoreMasterKey, SpAddAppRole, SpAddLinkedServer, SpAddLinkedSrvLogin, SpAddLogin, SpAppRolePassword,
        SpControlDbMasterKeyPassword, SpPassword, SpSetAppRole, SqlCmdConnect, VariableOrParameter,
    ];

    private PasswordForm(string name, string syntax) => (Name, Syntax) = (name, syntax);

    /// <summary>The member's name, which a test names its sample by.</summary>
    public string Name { get; }

    /// <summary>The form as the T-SQL reference writes it, which a finding quotes.</summary>
    public string Syntax { get; }

    public int CompareTo(PasswordForm? other) => string.CompareOrdinal(Name, other?.Name);

    public override string ToString() => Syntax;
}
