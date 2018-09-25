// Copyright 2018 Canaan Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Caliburn.Micro;

namespace Canaan.Kendryte.Flash.Shell
{
    public class AppBootstrapper : BootstrapperBase
    {
        private SimpleContainer _container;
        private bool _started = false;

        public AppBootstrapper()
        {
            Initialize();
        }

        protected override void Configure()
        {
            _container = new SimpleContainer();

            _container.Singleton<IWindowManager, WindowManager>();
            _container.Singleton<IEventAggregator, EventAggregator>();
            _container.PerRequest<IShell, ViewModels.ShellViewModel>();
        }

        protected override object GetInstance(Type service, string key)
        {
            return _container.GetInstance(service, key);
        }

        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return _container.GetAllInstances(service);
        }

        protected override void BuildUp(object instance)
        {
            _container.BuildUp(instance);
        }

        protected override void OnStartup(object sender, System.Windows.StartupEventArgs e)
        {
            DisplayRootViewFor<IShell>();
            _started = true;
        }

        protected override void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            var except = e.Exception;
            while (except is TargetInvocationException && except.InnerException != null)
                except = except.InnerException;

            MessageBox.Show(except.Message, "K-Flash", MessageBoxButton.OK, MessageBoxImage.Error);
            if (!_started)
                Application.Shutdown();
        }
    }
}