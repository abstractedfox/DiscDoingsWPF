using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
//using System.Threading.Tasks;

using System.Collections;

namespace DiscDoingsWPF
{
    //This was done to add built-in task throttling to the object without having to go back and change the rest of the code
    public class TaskContainer<Task> : ICollection
    {
        List<System.Threading.Tasks.Task> taskQueue = new List<System.Threading.Tasks.Task>();
        private bool _runningPendingTasks = false;
        
        private object _lockObject = new Object(), _pendingTaskObject = new Object();

        public int MaxTasks { get; set; } = 30;

        public System.Threading.Tasks.Task this[int i]
        {
            get {
                lock (_lockObject)
                {
                    return taskQueue[i];
                }
            }
            set
            {
                lock (_lockObject)
                {
                    taskQueue[i] = value;
                }
            }
        }

        public void Add(System.Threading.Tasks.Task itemToAdd)
        {
            lock (_lockObject)
            {
                taskQueue.Add(itemToAdd);
                //System.Windows.MessageBox.Show(itemToAdd.Status.ToString());
            }

            lock(_pendingTaskObject) if (!_runningPendingTasks) _runPendingTasks();
        }

        private async void _runPendingTasks()
        {
            lock (_pendingTaskObject)
            {
                if (!_runningPendingTasks) _runningPendingTasks = true;
                else return;
            }

            await System.Threading.Tasks.Task.Run(() =>
            {
                while (taskQueue.Count > 0) //Tasks are removed from the list as they finish
                {
                    while (TasksRunning() < MaxTasks)
                    {
                        lock (_lockObject)
                        {
                            var nextTaskQuery = taskQueue.Where(a => !a.IsCompleted && a.Status != TaskStatus.Running);
                            if (nextTaskQuery.Count() == 0) break;
                            System.Threading.Tasks.Task? nextTask = null;
                            try
                            {
                                nextTask = nextTaskQuery.First();
                            }
                            catch
                            {

                            }

                            if (nextTask != null)
                            {
                                try
                                {
                                    nextTask.Start();
                                }
                                catch
                                {

                                }
                            }
                            //note: probably will need some exception handling here
                        }
                    }
                }
                lock (_pendingTaskObject) _runningPendingTasks = false;
            });
        }

        public void RemoveAt(int index)
        {
            try
            {
                lock (_lockObject)
                {
                    taskQueue.RemoveAt(index);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        public void CopyTo(System.Threading.Tasks.Task[] array, int arrayIndex)
        {
            lock (_lockObject)
            {
                taskQueue.CopyTo(array, arrayIndex);
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            throw new NotImplementedException();
            //This is the only way to make the compiler happy
        }

        public int Count
        {
            get
            {
                lock (_lockObject)
                {
                    return taskQueue.Count;
                }
            }
        }


        public IEnumerator GetEnumerator()
        {
            lock (_lockObject)
            {
                return ((IEnumerable)taskQueue).GetEnumerator();
            }
        }


        public object SyncRoot
        {
            get
            {
                return this; //the documentation says to return the current instance
            }
        }

        public bool IsSynchronized
        {
            get
            {
                return false; //Change this to true if we decide to build locks into this class rather than places where it's used
            }
        }

        
        public int TasksRunning()
        {
            int activeTaskCount = 0;
            //int completedTaskCount = 0;
            lock (_lockObject)
            {
                for (int i = 0; i < taskQueue.Count(); i++)
                {
                    if (!taskQueue[i].IsCompleted && taskQueue[i].Status != TaskStatus.Created) activeTaskCount++;
                    //else if (taskQueue[i].IsCompleted) completedTaskCount++;
                    
                    else if (taskQueue[i].IsCompleted)
                    {
                        taskQueue.RemoveAt(i);
                        i--;
                    }

                    if (activeTaskCount == MaxTasks) break;
                }

                //Remove old tasks if the list is getting huge or if we appear to be finished processing new tasks
                /*
                if (completedTaskCount > 300 || activeTaskCount == 0)
                {
                    for (int i = 0; i < taskQueue.Count(); i++)
                    {
                        if (taskQueue[i].IsCompleted) taskQueue.RemoveAt(i);
                        if (i > 0) i--;
                    }
                }*/
            }

            return activeTaskCount;
        }


    }
}
