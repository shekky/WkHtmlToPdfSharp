﻿using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting;

namespace TuesPechkin
{
    /// <summary>
    /// Deployments loaded with this class must be marked Serializable.
    /// </summary>
    /// <typeparam name="TToolset">The type of toolset to manage remotely.</typeparam>
    public sealed class RemotingToolset<TToolset> : NestingToolset
        where TToolset : MarshalByRefObject, IToolset, new()
    {
        public RemotingToolset(IDeployment deployment)
        {
            Deployment = deployment ?? throw new ArgumentNullException(nameof(deployment));
        }

        public override void Load(IDeployment deployment = null)
        {
            if (Loaded)
            {
                return;
            }

            if (deployment != null)
            {
                Deployment = deployment;
            }

            SetupAppDomain();

            ObjectHandle handle = Activator.CreateInstanceFrom(
                remoteDomain,
                typeof(TToolset).Assembly.Location,
                typeof(TToolset).FullName,
                false,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                null,
                null,
                null);

            NestedToolset = (IToolset) handle.Unwrap();
            NestedToolset.Load(Deployment);
            Deployment = NestedToolset.Deployment;

            Loaded = true;
        }

        public override event EventHandler Unloaded;

        public override void Unload()
        {
            if (Loaded)
            {
                TearDownAppDomain(null, EventArgs.Empty);
            }
        }

        private AppDomain remoteDomain;

        private void SetupAppDomain()
        {
            var setup = AppDomain.CurrentDomain.SetupInformation;

            setup.LoaderOptimization = LoaderOptimization.SingleDomain;

            remoteDomain = AppDomain.CreateDomain(
                string.Format("tuespechkin_{0}", Guid.NewGuid()),
                null,
                setup);

            if (AppDomain.CurrentDomain.IsDefaultAppDomain() == false)
            {
                AppDomain.CurrentDomain.DomainUnload += TearDownAppDomain;
            }
        }

        private void TearDownAppDomain(object sender, EventArgs e)
        {
            if (remoteDomain == null)
            {
                return;
            }

            OnBeforeUnload((Action)(() => NestedToolset.Unload()));

            AppDomain.Unload(remoteDomain);

            var expected = Path.Combine(
                Deployment.Path, 
                WkhtmltoxBindings.DLLNAME);

            foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
            {
                if (module.FileName == expected)
                {
                    while (WinApiHelper.FreeLibrary(module.BaseAddress))
                    {
                    }

                    break;
                }
            }

            remoteDomain = null;
            Loaded = false;

            Unloaded?.Invoke(this, EventArgs.Empty);
        }
    }
}