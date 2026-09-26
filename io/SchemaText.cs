using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChange.Io;

/// <summary>
/// Schema text as it leaves the tool (decision 2.27, VALUES.md X2): the text with each value a known <see cref="PasswordForm"/>
/// sets replaced, whole literal token included, by <see cref="LeftOut"/>, and the forms found; or, when ScriptDom cannot parse the
/// text as a script, a scalar expression or a boolean expression, where the parse stopped, and the caller leaves the text out whole.
/// The printer is pure computation over the text; cli calls it while rendering, which is where text leaves the tool, and the model
/// keeps every script as written. Each SQLCMD directive line and each $(Name) is masked with characters of the same length before
/// the parse, so every offset holds in the text as given; a value that is a SQLCMD variable, <c>'$(Password)'</c>, is no literal,
/// stays, and names no form.
/// </summary>
public static class SchemaText
{
    /// <summary>What stands where a value a password form set stood.</summary>
    public const string LeftOut = "<left out>";

    private static readonly Regex Directive = new(@"^[ \t]*:", RegexOptions.CultureInvariant);

    private static readonly Regex Connect = new(@"^([ \t]*:connect\b.*?\s-P\s+)(\S+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex Variable = new(@"\$\(" + SqlCmdVariable.NamePattern + @"\)", RegexOptions.CultureInvariant);

    private static readonly Regex VariableLiteral = new(@"\A(?:N?['""])?\$\(" + SqlCmdVariable.NamePattern + @"\)['""]?\z", RegexOptions.CultureInvariant);

    private static readonly Regex Secretive = new(@"password|pwd|secret", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>The stored procedures that take a password: each with the form, the positions of its password parameters and their names.</summary>
    private static readonly Dictionary<string, (PasswordForm Form, int[] Positions, string[] Names)> Procedures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sp_password"] = (PasswordForm.SpPassword, [0, 1], ["@old", "@new"]),
        ["sp_addlogin"] = (PasswordForm.SpAddLogin, [1], ["@passwd"]),
        ["sp_approlepassword"] = (PasswordForm.SpAppRolePassword, [1], ["@newpwd"]),
        ["sp_addapprole"] = (PasswordForm.SpAddAppRole, [1], ["@password"]),
        ["sp_setapprole"] = (PasswordForm.SpSetAppRole, [1], ["@password"]),
        ["sp_addlinkedsrvlogin"] = (PasswordForm.SpAddLinkedSrvLogin, [4], ["@rmtpassword"]),
        ["sp_addlinkedserver"] = (PasswordForm.SpAddLinkedServer, [5], ["@provstr"]),
        ["sp_control_dbmasterkey_password"] = (PasswordForm.SpControlDbMasterKeyPassword, [1], ["@password"]),
    };

    /// <summary>The text printed: each value a form sets left out and the forms named, or where the parse stopped.</summary>
    public static PrintedScript Print(string text)
    {
        var (masked, connects) = Masked(text);
        var attempts = new List<(Func<TSqlParser, TextReader, (TSqlFragment? Fragment, IList<ParseError> Errors)> Parse, bool QuotedIdentifiers, bool VariablesAsLiterals)>
        {
            (Script, true, false), (Script, false, false), (Script, true, true), (Script, false, true), (Expression, true, false), (Expression, true, true), (Predicate, true, false),
        };
        ParseError? first = null;
        foreach (var (parse, quotedIdentifiers, variablesAsLiterals) in attempts)
        {
            var (fragment, errors) = parse(TSql.Parser(quotedIdentifiers), new StringReader(variablesAsLiterals ? Quoted(masked) : Lettered(masked)));
            if (errors.Count == 0 && fragment is not null)
            {
                return Printed(text, fragment, connects);
            }

            first ??= errors.FirstOrDefault();
        }

        return new PrintedScript.Unparsed(first?.Line ?? 1, first?.Column ?? 1);
    }

    private static (TSqlFragment? Fragment, IList<ParseError> Errors) Script(TSqlParser parser, TextReader reader) => (parser.Parse(reader, out var errors), errors);

    private static (TSqlFragment? Fragment, IList<ParseError> Errors) Expression(TSqlParser parser, TextReader reader) => (parser.ParseExpression(reader, out var errors), errors);

    private static (TSqlFragment? Fragment, IList<ParseError> Errors) Predicate(TSqlParser parser, TextReader reader) => (parser.ParseBooleanExpression(reader, out var errors), errors);

    /// <summary>The text with each literal a form sets replaced, from the last forward so earlier offsets hold, then each :connect line's -P value.</summary>
    private static PrintedScript Printed(string text, TSqlFragment fragment, IReadOnlyList<int> connects)
    {
        var found = new Passwords(text);
        fragment.Accept(found);
        var printed = new StringBuilder(text);
        foreach (var literal in found.Literals.DistinctBy(l => l.StartOffset).OrderByDescending(l => l.StartOffset))
        {
            printed.Remove(literal.StartOffset, literal.FragmentLength).Insert(literal.StartOffset, LeftOut);
        }

        var forms = new HashSet<PasswordForm>(found.Forms);
        var lines = printed.ToString().Split('\n');
        foreach (var at in connects)
        {
            var line = Connect.Replace(lines[at], m => m.Groups[1].Value + LeftOut);
            if (line != lines[at])
            {
                lines[at] = line;
                forms.Add(PasswordForm.SqlCmdConnect);
            }
        }

        return new PrintedScript.Printed(string.Join('\n', lines), SortedArray.Of(forms));
    }

    /// <summary>Each SQLCMD directive line (:setvar, :r, :on error, :connect) as spaces of the same length, and the indexes of the :connect lines.</summary>
    private static (string Masked, IReadOnlyList<int> Connects) Masked(string text)
    {
        var lines = text.Split('\n');
        var connects = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (Directive.IsMatch(lines[i]))
            {
                if (Connect.IsMatch(lines[i]))
                {
                    connects.Add(i);
                }

                lines[i] = new string(' ', lines[i].TrimEnd('\r').Length) + (lines[i].EndsWith('\r') ? "\r" : "");
            }
        }

        return (string.Join('\n', lines), connects);
    }

    /// <summary>Each $(Name) as letters of the same length, an identifier where the variable stands bare and part of the text inside quotes.</summary>
    private static string Lettered(string text) => Variable.Replace(text, m => new string('x', m.Length));

    /// <summary>Each $(Name) as a string literal of the same length, so a variable standing where a literal must stand parses; inside quotes, letters.</summary>
    private static string Quoted(string text) => Variable.Replace(text, m => m.Index > 0 && text[m.Index - 1] is '\'' or '"' or '['
        ? new string('x', m.Length)
        : "'" + new string('x', m.Length - 2) + "'");

    /// <summary>Every string literal a password form sets, with the form, over the parsed text; a literal that is a SQLCMD variable in the text as given is skipped.</summary>
    private sealed class Passwords(string text) : TSqlFragmentVisitor
    {
        public List<Literal> Literals { get; } = [];

        public List<PasswordForm> Forms { get; } = [];

        public override void ExplicitVisit(CreateLoginStatement node)
        {
            Found(PasswordForm.CreateLogin, (node.Source as PasswordCreateLoginSource)?.Password);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(AlterLoginOptionsStatement node)
        {
            Principal(PasswordForm.AlterLogin, node.Options);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateUserStatement node)
        {
            Principal(PasswordForm.CreateUser, node.UserOptions);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(AlterUserStatement node)
        {
            Principal(PasswordForm.AlterUser, node.UserOptions);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateApplicationRoleStatement node)
        {
            ApplicationRole(PasswordForm.CreateApplicationRole, node.ApplicationRoleOptions);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(AlterApplicationRoleStatement node)
        {
            ApplicationRole(PasswordForm.AlterApplicationRole, node.ApplicationRoleOptions);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateMasterKeyStatement node)
        {
            Found(PasswordForm.CreateMasterKey, node.Password);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(AlterMasterKeyStatement node)
        {
            Found(PasswordForm.AlterMasterKey, node.Password);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(OpenMasterKeyStatement node)
        {
            Found(PasswordForm.OpenMasterKey, node.Password);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(BackupMasterKeyStatement node)
        {
            Found(PasswordForm.BackupMasterKey, node.Password);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(RestoreMasterKeyStatement node)
        {
            Found(PasswordForm.RestoreMasterKey, node.Password, node.EncryptionPassword);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateAsymmetricKeyStatement node)
        {
            Found(PasswordForm.CreateAsymmetricKey, node.Password);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(AlterAsymmetricKeyStatement node)
        {
            Found(PasswordForm.AlterAsymmetricKey, node.EncryptionPassword, node.DecryptionPassword);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateCertificateStatement node)
        {
            Found(PasswordForm.CreateCertificate, node.EncryptionPassword, node.DecryptionPassword);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(AlterCertificateStatement node)
        {
            Found(PasswordForm.AlterCertificate, node.EncryptionPassword, node.DecryptionPassword);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(BackupCertificateStatement node)
        {
            Found(PasswordForm.BackupCertificate, node.EncryptionPassword, node.DecryptionPassword);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateSymmetricKeyStatement node)
        {
            Found(PasswordForm.CreateSymmetricKey, [
                .. node.KeyOptions.Select(o => o switch { KeySourceKeyOption k => k.PassPhrase, IdentityValueKeyOption i => i.IdentityPhrase, _ => null }),
                .. node.EncryptingMechanisms.Select(Password)]);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(AlterSymmetricKeyStatement node)
        {
            Found(PasswordForm.AlterSymmetricKey, [.. node.EncryptingMechanisms.Select(Password)]);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(OpenSymmetricKeyStatement node)
        {
            Found(PasswordForm.OpenSymmetricKey, Password(node.DecryptionMechanism));
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(AddSignatureStatement node)
        {
            Found(node.IsCounter ? PasswordForm.AddCounterSignature : PasswordForm.AddSignature, [.. node.Cryptos.Select(Password)]);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateCredentialStatement node)
        {
            Found(node.IsDatabaseScoped ? PasswordForm.CreateDatabaseScopedCredential : PasswordForm.CreateCredential, node.Secret);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(AlterCredentialStatement node)
        {
            Found(node.IsDatabaseScoped ? PasswordForm.AlterDatabaseScopedCredential : PasswordForm.AlterCredential, node.Secret);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateExternalDataSourceStatement node)
        {
            ExternalDataSource(node.ExternalDataSourceOptions);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(AlterExternalDataSourceStatement node)
        {
            ExternalDataSource(node.ExternalDataSourceOptions);
            base.ExplicitVisit(node);
        }

        /// <summary>RESTORE keeps PASSWORD and MEDIAPASSWORD for backups made with them; BACKUP lost both in SQL Server 2012, and the 2022 grammar reads neither, so such a script is left out whole as unparsed.</summary>
        public override void ExplicitVisit(RestoreStatement node)
        {
            Found(PasswordForm.Restore, [.. node.Options.Where(o => o.OptionKind is RestoreOptionKind.Password or RestoreOptionKind.MediaPassword)
                .Select(o => (o as ScalarExpressionRestoreOption)?.Value as Literal)]);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(OpenRowsetTableReference node)
        {
            Found(PasswordForm.OpenRowset, node.Password, node.ProviderString);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(AdHocDataSource node)
        {
            Found(PasswordForm.OpenDataSource, node.InitString);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(ExecuteStatement node)
        {
            if (node.ExecuteSpecification.ExecutableEntity is ExecutableProcedureReference procedure)
            {
                var name = procedure.ProcedureReference?.ProcedureReference?.Name.BaseIdentifier?.Value;
                var parameters = procedure.Parameters;
                if (name is not null && Procedures.TryGetValue(name, out var known))
                {
                    var positional = parameters.Where(p => p.Variable is null).ToList();
                    Found(known.Form, [
                        .. known.Positions.Where(i => i < positional.Count).Select(i => positional[i].ParameterValue as Literal),
                        .. parameters.Where(p => p.Variable is not null && known.Names.Contains(p.Variable.Name, StringComparer.OrdinalIgnoreCase)).Select(p => p.ParameterValue as Literal)]);
                }
                else
                {
                    Found(PasswordForm.VariableOrParameter, [.. parameters.Where(p => p.Variable is not null && Secretive.IsMatch(p.Variable.Name)).Select(p => p.ParameterValue as Literal)]);
                }
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(DeclareVariableElement node)
        {
            if (Secretive.IsMatch(node.VariableName.Value))
            {
                Found(PasswordForm.VariableOrParameter, node.Value as Literal);
            }

            base.ExplicitVisit(node);
        }

        /// <summary>A procedure's or a function's parameter, a variable of its own kind to the visitor: its default is the literal the reviewers found.</summary>
        public override void ExplicitVisit(ProcedureParameter node)
        {
            if (Secretive.IsMatch(node.VariableName.Value))
            {
                Found(PasswordForm.VariableOrParameter, node.Value as Literal);
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(SetVariableStatement node)
        {
            if (Secretive.IsMatch(node.Variable.Name))
            {
                Found(PasswordForm.VariableOrParameter, node.Expression as Literal);
            }

            base.ExplicitVisit(node);
        }

        private void Principal(PasswordForm form, IEnumerable<PrincipalOption> options) => Found(form, [.. options.SelectMany(o => o switch
        {
            PasswordAlterPrincipalOption p => (Literal?[])[p.Password, p.OldPassword],
            LiteralPrincipalOption { OptionKind: PrincipalOptionKind.Password } l => [l.Value],
            _ => [],
        })]);

        private void ApplicationRole(PasswordForm form, IEnumerable<ApplicationRoleOption> options) =>
            Found(form, [.. options.Where(o => o.OptionKind == ApplicationRoleOptionKind.Password).Select(o => o.Value?.ValueExpression as Literal)]);

        private void ExternalDataSource(IEnumerable<ExternalDataSourceOption> options) => Found(PasswordForm.CreateExternalDataSource,
            [.. options.OfType<ExternalDataSourceLiteralOrIdentifierOption>().Where(o => o.OptionKind == ExternalDataSourceOptionKind.ConnectionOptions).Select(o => o.Value?.ValueExpression as Literal)]);

        /// <summary>A mechanism's password: the literal of a PASSWORD mechanism, or the string a certificate or key is given WITH PASSWORD (a signature is a binary literal).</summary>
        private static Literal? Password(CryptoMechanism? mechanism) => mechanism?.PasswordOrSignature is { LiteralType: LiteralType.String } password ? password : null;

        private void Found(PasswordForm form, params Literal?[] literals)
        {
            var set = literals.OfType<Literal>().Where(l => l.LiteralType == LiteralType.String && !VariableLiteral.IsMatch(text.Substring(l.StartOffset, l.FragmentLength))).ToList();
            if (set.Count > 0)
            {
                Literals.AddRange(set);
                Forms.Add(form);
            }
        }
    }
}

/// <summary>Schema text as the printer leaves it, closed: printed with each password value left out and the forms named, or unparsed, with where ScriptDom's parse stopped.</summary>
public abstract record PrintedScript
{
    private PrintedScript()
    {
    }

    /// <summary>The text printed, each value a form set replaced by SchemaText.LeftOut, and the forms found, each once.</summary>
    public sealed record Printed(string Text, SortedArray<PasswordForm> Forms) : PrintedScript;

    /// <summary>Text ScriptDom cannot parse, so a password in it cannot be found; the line and column its parse stopped at, one-based.</summary>
    public sealed record Unparsed(int Line, int Column) : PrintedScript;
}
