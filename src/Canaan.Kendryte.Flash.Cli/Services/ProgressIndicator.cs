using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using ShellProgressBar;

namespace Canaan.Kendryte.Flash.Cli.Services
{
    internal class ProgressIndicator
    {
        private readonly ProgressBar _progressBar;
        private JobItemStatus _jobItem;

        public ProgressIndicator()
        {
            if (!Console.IsInputRedirected && !Console.IsOutputRedirected)
            {
                _progressBar = new ProgressBar(100, "Flash", new ProgressBarOptions
                {
                });
            }
        }

        public void SetJobItem(JobItemType itemType, JobItemStatus jobItem)
        {
            if(_jobItem != null)
                _jobItem.PropertyChanged -= JobItem_PropertyChanged;

            _jobItem = jobItem;
            jobItem.PropertyChanged += JobItem_PropertyChanged;
            _progressBar?.Tick(itemType.ToString());
        }

        private void JobItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(JobItemStatus.Progress):
                    _progressBar?.Tick((int)(_jobItem.Progress * 100));
                    break;
                default:
                    break;
            }
        }
    }
}
