using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using System.Collections;

namespace RIP
{
    public class ExpirableList<T> : IList<T>
    {
        public System.Threading.Mutex mutex = new System.Threading.Mutex(); 
        private volatile List<Tuple<DateTime, T>> collection = new List<Tuple<DateTime,T>>();

        private Timer timer;

        public double Interval
        {
            get { return timer.Interval; }
            set { timer.Interval = value; }
        }

        private TimeSpan expiration;

        public TimeSpan Expiration
        {
            get { return expiration; }
            set { expiration = value; }
        }

        /// <summary>
        /// Define a list that automaticly remove expired objects.
        /// </summary>
        /// <param name="_interval"></param>
        /// The interval at which the list test for old objects.
        /// <param name="_expiration"></param>
        /// The TimeSpan an object stay valid inside the list.
        public ExpirableList(int _interval, TimeSpan _expiration)
        {
            timer = new Timer();
            timer.Interval = _interval;
            timer.Elapsed += Tick;
            timer.Start();

            expiration = _expiration;
        }

        private void Tick(object sender, EventArgs e)
        {
            mutex.WaitOne();
            for (int i = collection.Count - 1; i >= 0; i--)
            {
                if ((DateTime.Now - collection[i].Item1) >= expiration)
                {
                    collection.RemoveAt(i);
                }
            }
            mutex.ReleaseMutex();
        }

        public void PrintItems()
        {
            mutex.WaitOne();
            for (int i = collection.Count - 1; i >= 0; i--)
            {
                Console.WriteLine(collection[i].Item2.ToString());
            }
            mutex.ReleaseMutex();
        }

        public void RemoveExpired()
        {
            for (int i = collection.Count - 1; i >= 0; i--)
            {
                if ((DateTime.Now - collection[i].Item1) >= expiration)
                {
                    collection.RemoveAt(i);
                }
            }
        }

        #region IList Implementation
        public T this[int index]
        {
            get { return collection[index].Item2; }
            set { collection[index] = new Tuple<DateTime, T>(DateTime.Now, value); }
        }

        public Tuple<DateTime, T> this[int index, bool returnTuple]
        {
            get { return collection[index]; }
        }

        public IEnumerator<T> GetEnumerator()
        {
            return collection.Select(x => x.Item2).GetEnumerator();
        }

        public List<T> AsNormalList(){
            return collection.Select(x => x.Item2).ToList();
        }

        public void Add(T item)
        {
            collection.Add(new Tuple<DateTime, T>(DateTime.Now, item));
        }

        public int Count
        {
            get { return collection.Count; }
        }

        public bool IsSynchronized
        {
            get { return false; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public void CopyTo(T[] array, int index)
        {
            for (int i = 0; i < collection.Count; i++)
                array[i + index] = collection[i].Item2;
        }

        public bool Remove(T item)
        {
            bool contained = Contains(item);
            for (int i = collection.Count - 1; i >= 0; i--)
            {
                if (collection[i].Item2.Equals(item))
                    collection.RemoveAt(i);
            }
            return contained;
        }

        public void RemoveAt(int i)
        {
            collection.RemoveAt(i);
        }

        public bool Contains(T item)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                if (collection[i].Item2.Equals(item))
                    return true;
            }

            return false;
        }

        public void Insert(int index, T item)
        {
            collection.Insert(index, new Tuple<DateTime, T>(DateTime.Now, item));
        }

        public int IndexOf(T item)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                if (collection[i].Item2.Equals(item))
                    return i;
            }

            return -1;
        }

        public T Find (Predicate<T> match)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                if (match(collection[i].Item2))
                {
                    return collection[i].Item2;
                }
                    
            }
            return default(T);
        }

        public void Clear()
        {
            collection.Clear();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return collection.Select(x => x.Item2).GetEnumerator();
        }
        #endregion
    }
}