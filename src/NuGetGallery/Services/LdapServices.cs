﻿using System;
using System.Configuration;
using System.DirectoryServices;
using System.Linq;

namespace NuGetGallery
{
    public class LdapService : ILdapService
    {
        //readonly ICryptographyService cryptoSvc;
        readonly IEntityRepository<User> userRepo;
        protected LdapService() { }
        public LdapService(IEntityRepository<User> userRepo)
            : this()
        {
            //this.cryptoSvc = cryptoSvc;
            this.userRepo = userRepo;
        }

        public bool Enabled
        {
            get { return !string.IsNullOrWhiteSpace(Uri); }
        }


        //TODO: need to likely move this to configuration service injection. 
        public string Domain
        {
            get { return ConfigurationManager.AppSettings.Get("Ldap:Domain"); }
        }

        private static string Uri
        {
            get { return ConfigurationManager.AppSettings.Get("Ldap:Uri"); }
        }

        private static string UserBase
        {
            get { return ConfigurationManager.AppSettings.Get("Ldap:UserBase"); }
        }

        public static class Properties
        {
            public static string Username
            {
                get { return ConfigurationManager.AppSettings.Get("Ldap:Prop:Username") ?? "samaccountname"; }
            }

            public static string DisplayName
            {
                get { return ConfigurationManager.AppSettings.Get("Ldap:Prop:DisplayName") ?? "displayname"; }
            }

            public static string Email
            {
                get { return ConfigurationManager.AppSettings.Get("Ldap:Prop:Email") ?? "mail"; }
            }
        }

        private DirectoryEntry GetDirectoryEntry(string username, string password)
        {
            return new DirectoryEntry(string.Concat(Uri, '/', UserBase), string.Concat(Domain, '\\', username), password);
        }

        public SearchResult GetUserSearchResult(string username, string password)
        {
            return new DirectorySearcher(GetDirectoryEntry(username, password), string.Format("({0}={1})", Properties.Username, username), new[] { Properties.Username, Properties.DisplayName, Properties.Email }).FindOne();
        }

        public bool ValidateUser(string username, string password)
        {
            return ValidateUser(GetDirectoryEntry(username, password));
        }

        private static bool ValidateUser(DirectoryEntry directoryEntry)
        {
            try
            {
                var nativeObject = directoryEntry.NativeObject;
                return nativeObject != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// If users exists in LDAP, creates instace of user and returns that user object.
        /// </summary>
        /// <param name="username">username or email of the user</param>
        /// <param name="password">password in plaintext</param>
        /// <returns></returns>
        public User AutoEnroll(string username, string password)
        {
            if (!Enabled)
                return null;
            if (username.StartsWith(Domain + '\\', StringComparison.OrdinalIgnoreCase))
                username = username.Substring(Domain.Length + 2);

            if (ValidateUser(username, password))
            {
                var searchResult = GetUserSearchResult(username, password);
                var user = new User(GetStringProperty(searchResult, Properties.Username).ToLowerInvariant(), ".")
                {
                    ApiKey = Guid.NewGuid(),
                    //DisplayName = GetStringProperty(searchResult, Properties.DisplayName),
                    PasswordHashAlgorithm = "LDAP"
                };
                var email = GetStringProperty(searchResult, Properties.Email);
                if (!string.IsNullOrEmpty(email))
                {
                    user.EmailAllowed = true;
                    user.UnconfirmedEmailAddress = email.ToLowerInvariant();
                    user.EmailConfirmationToken = CryptographyService.GenerateToken();
                    user.ConfirmEmailAddress();
                }
                userRepo.InsertOnCommit(user);
                userRepo.CommitChanges();
                return user;
            }
            return null;
        }

        public User GetUserByUsernameOrEmailAndPassord(string userName, string password)
        {
            throw new NotImplementedException();
        }

        private static string GetStringProperty(SearchResult searchResult, string propertyName)
        {
            return searchResult.Properties[propertyName].Cast<string>().First();
        }
    }
}