using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>The secrets a model's properties can hold, planted in each and kept out of every element property, and DacFx's text properties
/// each listed as a secret or reviewed as not one.</summary>
public sealed partial class ModelElementsTests
{
    /// <summary>
    /// A model holding one of each object that carries a secret, each planted with one value: a login's, a contained user's and an
    /// application role's password; an asymmetric key's, a certificate's (its key's and its private key file's) and a symmetric
    /// key's password; a symmetric key's KEY_SOURCE and IDENTITY_VALUE, from which SQL Server derives the key; a server credential's
    /// and a database scoped credential's secret; the database master key's password; a signature's password; a linked server
    /// login's password; a linked server's provider string (sp_addlinkedserver's @provstr) and an external data source's
    /// CONNECTION_OPTIONS, each an ODBC or OLE DB connection string carrying PWD=. DacFx gives the planted value back from every
    /// property <see cref="Ssdt.Secrets"/> lists, and Ssdt.ReadModel carries it in no property.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "X2")]
    public void A_secret_planted_in_every_property_that_holds_one_reaches_no_element_property()
    {
        const string Planted = "Pl4nted!secret#7f3a";
        using var model = Model(new TSqlModelOptions { Containment = Containment.Partial },
            "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY);",
            "CREATE PROCEDURE dbo.P AS SELECT 1 AS One;",
            "CREATE LOGIN L WITH PASSWORD = '" + Planted + "';",
            "CREATE USER U WITH PASSWORD = '" + Planted + "';",
            "CREATE APPLICATION ROLE AR WITH PASSWORD = '" + Planted + "';",
            "CREATE MASTER KEY ENCRYPTION BY PASSWORD = '" + Planted + "';",
            "CREATE ASYMMETRIC KEY AK WITH ALGORITHM = RSA_2048 ENCRYPTION BY PASSWORD = '" + Planted + "';",
            "CREATE CERTIFICATE C1 ENCRYPTION BY PASSWORD = '" + Planted + "' WITH SUBJECT = 'c1';",
            "CREATE CERTIFICATE C2 FROM FILE = 'c2.cer' WITH PRIVATE KEY (FILE = 'c2.pvk', DECRYPTION BY PASSWORD = '" + Planted
                + "', ENCRYPTION BY PASSWORD = '" + Planted + "');",
            "CREATE SYMMETRIC KEY SK WITH ALGORITHM = AES_256, KEY_SOURCE = '" + Planted + "ks', IDENTITY_VALUE = '" + Planted
                + "iv' ENCRYPTION BY PASSWORD = '" + Planted + "';",
            "CREATE CREDENTIAL CR WITH IDENTITY = 'i', SECRET = '" + Planted + "';",
            "CREATE DATABASE SCOPED CREDENTIAL DC WITH IDENTITY = 'i', SECRET = '" + Planted + "';",
            "CREATE EXTERNAL DATA SOURCE EDS WITH (LOCATION = 'sqlserver://remote', CONNECTION_OPTIONS = 'Server=remote;UID=u;PWD="
                + Planted + "', CREDENTIAL = DC);",
            "ADD SIGNATURE TO dbo.P BY CERTIFICATE C1 WITH PASSWORD = '" + Planted + "';",
            "EXECUTE sp_addlinkedserver @server = N'LS', @srvproduct = N'', @provider = N'MSOLEDBSQL', @datasrc = N'remote', @provstr = N'UID=u;PWD="
                + Planted + "';",
            "EXECUTE sp_addlinkedsrvlogin @rmtsrvname = N'LS', @useself = N'FALSE', @locallogin = NULL, @rmtuser = N'u', @rmtpassword = N'"
                + Planted + "';");

        var elements = Ok(Ssdt.ReadModel(model));

        Assert.All(Ssdt.Secrets, secret => Assert.True(Held(model, secret, Planted), Qualified(secret) + " holds no planted value"));
        Assert.Contains(elements, e => e.Key.ToString() == "Login [L]");
        Assert.Contains(elements, e => e.Key.ToString() == "LinkedServer [LS]");
        Assert.DoesNotContain(elements.SelectMany(e => e.Properties),
            p => Content(p.Value) is { } text && text.Contains(Planted, StringComparison.Ordinal));
    }

    /// <summary>
    /// Every string-typed property DacFx 170.5.96's model declares, each reviewed on 2026-09-25 as a secret or as none, with the
    /// reason. A property a later DacFx adds is on neither list, and the test names it; it is reviewed and listed before the upgrade
    /// lands. The review covers string-typed properties only: a SqlScriptProperty-typed one (Parameter.DefaultExpression,
    /// ExtendedProperty.Value, Table.QueryScript and the rest) is schema text, and Ssdt.ReadModel keeps it as written, as it does a
    /// module's Definition and a deploy script, until the operator's decision 2.27 on schema text that sets a password.
    /// </summary>
    private static readonly Dictionary<string, string[]> NotSecret = new(StringComparer.Ordinal)
    {
        ["a name, an address, a file or a path the definition gives, which SQL Server shows to a reader holding VIEW DEFINITION"] =
        [
            "Aggregate.ClassName", "AsymmetricKey.ExecutableFile", "AsymmetricKey.File", "AsymmetricKey.ProviderKeyName",
            "BrokerPriority.RemoteServiceName", "Certificate.ExistingKeysFilePath", "Certificate.PrivateKeyFilePath", "Certificate.Subject",
            "ClrTableOption.ClassName", "ClrTypeMethod.Name", "ClrTypeMethodParameter.Name", "ClrTypeProperty.Name",
            "Column.EncryptionAlgorithmName", "ColumnMasterKey.KeyPath", "ColumnMasterKey.KeyStoreProviderName", "Credential.Identity",
            "CryptographicProvider.DllPath", "DatabaseCredential.Identity", "DatabaseDdlTrigger.ClassName", "DatabaseDdlTrigger.MethodName",
            "DatabaseEventNotification.BrokerInstanceSpecifier", "DatabaseEventNotification.BrokerService",
            "DatabaseOptions.DefaultFullTextLanguage", "DatabaseOptions.DefaultLanguage", "DatabaseOptions.FileStreamDirectoryName",
            "DmlTrigger.ClassName", "DmlTrigger.MethodName", "ErrorMessage.Language", "EventSessionAction.ActionName",
            "EventSessionAction.EventPackageName", "EventSessionDefinitions.EventName", "EventSessionDefinitions.EventPackageName",
            "EventSessionSetting.SettingName", "EventSessionTarget.EventPackageName", "EventSessionTarget.TargetName",
            "ExternalDataSource.DatabaseName", "ExternalDataSource.Location", "ExternalDataSource.ResourceManagerLocation",
            "ExternalDataSource.ShardMapName", "ExternalLanguage.LanguageName", "ExternalLanguageFile.FileName",
            "ExternalLanguageFile.Path", "ExternalLanguageFile.Platform", "ExternalLibrary.Language", "ExternalLibrary.LibraryName",
            "ExternalLibraryFile.Path", "ExternalLibraryFile.Platform", "ExternalModel.LocalRuntimePath", "ExternalModel.Location",
            "ExternalModel.ModelNameExternal", "ExternalStream.Location", "ExternalTable.ExternalObjectName",
            "ExternalTable.ExternalSchemaName", "ExternalTable.Location", "ExternalTable.RejectedRowLocation",
            "FileTable.FileTableDirectory", "FullTextCatalog.Path", "HttpProtocolSpecifier.AuthenticationRealm",
            "HttpProtocolSpecifier.DefaultLogonDomain", "HttpProtocolSpecifier.Path", "HttpProtocolSpecifier.Website",
            "LinkedServer.Catalog", "LinkedServer.DataSource", "LinkedServer.Location", "LinkedServer.ProductName",
            "LinkedServer.ProviderName", "LinkedServerLogin.LinkedServerLoginName", "Login.DefaultDatabase", "Login.DefaultLanguage",
            "Procedure.ClassName", "Procedure.MethodName", "PromotedNodePathForSqlType.NodePath", "PromotedNodePathForXQueryType.NodePath",
            "PromotedNodePathForXQueryType.Type", "QueueEventNotification.BrokerInstanceSpecifier", "QueueEventNotification.BrokerService",
            "RemoteServiceBinding.Service", "Route.Address", "Route.BrokerInstance", "Route.MirrorAddress", "Route.ServiceName",
            "ScalarFunction.ClassName", "ScalarFunction.FillRowMethodName", "ScalarFunction.MethodName", "SearchProperty.Description",
            "ServerAudit.FilePath", "ServerAudit.Path", "ServerDdlTrigger.ClassName", "ServerDdlTrigger.MethodName",
            "ServerEventNotification.BrokerInstanceSpecifier", "ServerEventNotification.BrokerService",
            "SoapLanguageSpecifier.DatabaseName", "SoapLanguageSpecifier.Namespace", "SoapLanguageSpecifier.WsdlSpName",
            "SoapMethodSpecification.WebMethodAlias", "SoapMethodSpecification.WebMethodNamespace", "SqlFile.FileName",
            "SymmetricKey.ProviderKeyName", "Table.LedgerHistoryTableName", "Table.LedgerHistoryTableSchemaName", "Table.LedgerViewName",
            "Table.LedgerViewOperationTypeColumnName", "Table.LedgerViewOperationTypeDescColumnName", "Table.LedgerViewSchemaName",
            "Table.LedgerViewSequenceNumberColumnName", "Table.LedgerViewTransactionIdColumnName", "TableValuedFunction.ClassName",
            "TableValuedFunction.FillRowMethodName", "TableValuedFunction.MethodName", "TableValuedFunction.ReturnTableVariableName",
            "TcpProtocolSpecifier.ListenerIPv4", "TcpProtocolSpecifier.ListenerIPv6", "User.DefaultLanguage", "UserDefinedType.ClassName",
            "UserDefinedType.ValidationMethodName", "WorkloadClassifier.MemberName", "WorkloadClassifier.WlmContext",
            "WorkloadClassifier.WlmLabel", "XmlNamespace.NamespaceUri", "XmlNamespace.Prefix",
        ],
        ["a setting, a number, a date, a label or a statement the definition states, with no password or key in its documented form"] =
        [
            "Certificate.ExpiryDate", "Certificate.StartDate", "Column.Collation", "Column.IdentityIncrement", "Column.IdentitySeed",
            "Column.MaskingFunction", "Column.SensitivityInformationType", "Column.SensitivityInformationTypeId", "Column.SensitivityLabel",
            "Column.SensitivityLabelId", "ColumnEncryptionKeyValue.EncryptionAlgorithm", "DatabaseOptions.Collation",
            "ErrorMessage.MessageText", "ExternalFileFormat.DataCompression", "ExternalFileFormat.DateFormat",
            "ExternalFileFormat.Encoding", "ExternalFileFormat.FieldTerminator", "ExternalFileFormat.ParserVersion",
            "ExternalFileFormat.SerDeMethod", "ExternalFileFormat.StringDelimiter", "ExternalLanguageFile.EnvironmentVariables",
            "ExternalLanguageFile.Parameters", "ExternalModel.ApiFormat", "ExternalModel.Parameters", "ExternalStream.InputOptions",
            "ExternalStream.OutputOptions", "ExternalStreamingJob.Statement", "ExternalTable.TableOptions",
            "FileTable.FileTableCollateFilename", "LinkedServer.CollationName", "Sequence.IncrementValue", "Sequence.MaxValue",
            "Sequence.MinValue", "Sequence.StartValue", "TableTypeColumn.Collation", "TableTypeColumn.IdentityIncrement",
            "TableTypeColumn.IdentitySeed", "WorkloadClassifier.EndTime", "WorkloadClassifier.StartTime",
        ],
        ["a value SQL Server generates and shows in its catalog views: a GUID, a SID, a signature, a statistics blob, a key value encrypted by its column master key"] =
        [
            "ColumnEncryptionKeyValue.EncryptedValue", "ColumnMasterKey.Signature", "EventSessionAction.EventModuleGuid",
            "EventSessionDefinitions.EventModuleGuid", "EventSessionTarget.EventModuleGuid", "Login.Sid", "SearchProperty.PropertySetGuid",
            "ServerAudit.AuditGuid", "SignatureEncryptionMechanism.SignedBlob", "Statistics.StatsStream", "User.Sid",
        ],
    };

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "X2")]
    public void Every_text_property_DacFx_declares_is_a_listed_secret_or_reviewed_as_not_a_secret()
    {
        var reviewed = NotSecret.Values.SelectMany(names => names).ToList();
        var declared = typeof(ModelSchema).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => f.GetValue(null))
            .OfType<ModelTypeClass>()
            .SelectMany(t => t.Properties.Concat(t.Relationships.SelectMany(r => r.Properties))).Where(p => p.DataType == typeof(string))
            .Distinct().ToList();

        Assert.Empty(declared.Where(p => !Ssdt.Secrets.Contains(p) && !reviewed.Contains(Qualified(p))).Select(Qualified));
        Assert.Empty(reviewed.Except(declared.Select(Qualified)));
        Assert.Empty(reviewed.Intersect(Ssdt.Secrets.Select(Qualified)));
        Assert.Equal(reviewed.Count, reviewed.Distinct().Count());
    }

    /// <summary>A property as Type.Property, a relationship's as Relationship.Property.</summary>
    private static string Qualified(ModelPropertyClass p) => (p.OwningType?.Name ?? p.OwningRelationship?.Name) + "." + p.Name;
}
