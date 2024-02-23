using System;
using System.Collections.Generic;

namespace CitizenFX.MsgPack.Detail
{
	/// <summary>
	/// It's like a <see cref="List{T}" /> but allows for moving in an array (i.e.: no copy) and faster deletion by sacrificing ordered-ness
	/// </summary>
	/// <remarks>Supplied arrays must not be used anywhere else, mind you a copy is not being made</remarks>
	internal class DynamicArray<T>
	{
		private T[] array;
		public int Count { get; private set; }

		/// <inheritdoc cref="DynamicArray{T}"/>
		/// <param name="moveArray">array to be used internally</param>
		public DynamicArray(T[] moveArray)
		{
			array = moveArray;
			Count = moveArray.Length;
		}

		/// <inheritdoc cref="DynamicArray(T[])"/>
		public DynamicArray(ref T[] moveArray)
		{
			array = moveArray;
			Count = moveArray.Length;
			moveArray = null;
		}

		public T this[int index]
		{
			get => index < Count ? array[index] : throw new IndexOutOfRangeException();
			set => array[index] = index < Count ? value : throw new IndexOutOfRangeException();
		}

		public T this[uint index]
		{
			get => index < Count ? array[index] : throw new IndexOutOfRangeException();
			set => array[index] = index < Count ? value : throw new IndexOutOfRangeException();
		}

		public T[] AcquireArray()
		{
			T[] arr = array;
			array = null;
			Count = 0;
			return arr;
		}

		public void RemoveAt(uint index)
		{
			if (index >= Count)
				throw new IndexOutOfRangeException();

			array[index] = array[--Count];
		}

		public void RemoveAt(int index) => RemoveAt((uint)index);

		public bool Remove(T value)
		{
			for (int i = 0; i < Count; ++i)
			{
				if (value.Equals(array[i]))
				{
					array[i] = array[--Count];
					return true;
				}
			}

			return false;
		}

		public void RemoveAll(Func<T, bool> predicate)
		{
			for (uint i = 0; i < Count;)
			{
				if (predicate(array[i]))
					RemoveAt(i);
				else
					++i;
			}			
		}

		public void Add(T value)
		{
			EnsureCapacity(Count + 1);
			array[Count] = value;
			++Count;
		}

		public void Sort(Func<T, T, long> predicate)
		{
			QuickSort(predicate, 0, Count - 1);
		}

		private void QuickSort(Func<T, T, long> predicate, int leftIndex, int rightIndex)
		{
			int l = leftIndex;

			while (leftIndex < rightIndex)
			{
				int r = rightIndex, pivotIndex = (leftIndex + rightIndex) / 2;
				T pivot = array[pivotIndex];

				do
				{
					for (; l != pivotIndex && predicate(array[l], pivot) < 0; ++l) ; // find first to move from left
					for (; r != pivotIndex && predicate(pivot, array[r]) < 0; --r) ; // find first to move from right

					if (l == r)
					{
						++l;
						--r;
						break;
					}
					else if (l < r)
					{
						T temp = array[l];
						array[l++] = array[r];
						array[r--] = temp;
					}
				}
				while (l < r);

				if (leftIndex < r)
					QuickSort(predicate, leftIndex, r);

				leftIndex = l;
			}
		}

		private void EnsureCapacity(int minimum)
		{
			if (array.Length < minimum)
			{
				int newCount = array.Length == 0 ? 4 : array.Length * 2;
				if ((uint)newCount > 2146435071u)
				{
					newCount = 2146435071;
				}

				if (newCount < minimum)
				{
					newCount = minimum;
				}

				T[] oldArray = array;
				array = new T[newCount];

				for (int i = 0; i < oldArray.Length; ++i)
				{
					array[i] = oldArray[i];
				}
			}
		}
	}
}
