﻿//#define PROFILE

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Vexe.Editor.Types;
using Vexe.Editor.Windows;
using Vexe.Runtime.Extensions;
using Vexe.Runtime.Helpers;
using Vexe.Runtime.Types;
using UnityObject = UnityEngine.Object;

namespace Vexe.Editor.Drawers
{
	public abstract class SequenceDrawer<TSequence, TElement> : ObjectDrawer<TSequence> where TSequence : IList<TElement>
	{
		private readonly Type _elementType;
		private int _advancedKey;
		private List<EditorMember> _elements;
		private SequenceOptions _options;
		private bool _perItemDrawing;
		private bool _shouldDrawAddingArea;
		private int _newSize;

		private bool isAdvancedChecked
		{
			get { return prefs.Bools.ValueOrDefault(_advancedKey); }
			set { prefs.Bools[_advancedKey] = value; }
		}

		protected abstract TSequence GetNew();

		public SequenceDrawer()
		{
			_elementType = typeof(TElement);
			_elements    = new List<EditorMember>();
		}

		protected override void Initialize()
		{
			var displayAttr = attributes.GetAttribute<DisplayAttribute>();
			_options        = new SequenceOptions(displayAttr != null ? displayAttr.SeqOpt : Seq.None);

            if (_options.Readonly)
                displayText += " (Readonly)";

			_advancedKey          = RuntimeHelper.CombineHashCodes(id, "advanced");
			_perItemDrawing       = attributes.AnyIs<PerItemAttribute>();
			_shouldDrawAddingArea = !_options.Readonly && _elementType.IsA<UnityObject>();
		}

		public override void OnGUI()
		{
			if (memberValue == null)
				memberValue = GetNew();

			bool showAdvanced = _options.Advanced && !_options.Readonly;

			// header
			using (gui.Horizontal())
			{
				foldout = gui.Foldout(displayText, foldout, Layout.sExpandWidth());

				gui.FlexibleSpace();

				if (showAdvanced)
					isAdvancedChecked = gui.CheckButton(isAdvancedChecked, "advanced mode");

				if (!_options.Readonly)
				{
					using (gui.State(memberValue.Count > 0))
					{
						if (gui.ClearButton("elements"))
							Clear();
						if (gui.RemoveButton("last element"))
							RemoveLast();
					}
					if (gui.AddButton("element", MiniButtonStyle.ModRight))
						AddValue();
				}
			}

			if (!foldout) return;

			if (memberValue.IsEmpty())
			{
                using(gui.Indent())
				    gui.HelpBox("Sequence is empty");
				return;
			}

			// body
			using (gui.Vertical(_options.GuiBox ? GUI.skin.box : GUIStyle.none))
			{
				// advanced area
				if (isAdvancedChecked)
				{
					using (gui.Indent((GUI.skin.box)))
					{
						using (gui.Horizontal())
						{
							_newSize = gui.Int("New size", _newSize);
							if (gui.MiniButton("c", "Commit", MiniButtonStyle.ModRight))
							{
								if (_newSize != memberValue.Count)
									memberValue.AdjustSize(_newSize, RemoveAt, AddValue);
							}
						}

						using (gui.Horizontal())
						{
							gui.Label("Commands");

							if (gui.MiniButton("Shuffle", "Shuffle list (randomize the order of the list's elements", (Layout)null))
								Shuffle();

							if (gui.MoveDownButton())
								memberValue.Shift(true);

							if (gui.MoveUpButton())
								memberValue.Shift(false);

							if (!_elementType.IsValueType && gui.MiniButton("N", "Filter nulls"))
							{
								for (int i = memberValue.Count - 1; i > -1; i--)
									if (memberValue[i] == null)
										RemoveAt(i);
							}
						}
					}
				}

				using (gui.Indent(_options.GuiBox ? GUI.skin.box : GUIStyle.none))
				{
#if PROFILE
					Profiler.BeginSample("Sequence Elements");
#endif
					for (int iLoop = 0; iLoop < memberValue.Count; iLoop++)
					{
						var i = iLoop;
						var elementValue = memberValue[i];

						using (gui.Horizontal())
						{
							if (_options.LineNumbers)
							{
								gui.NumericLabel(i);
							}

							var previous = elementValue;

							gui.BeginCheck();
							{
								using (gui.Vertical())
								{
									var element = GetElement(i);
									gui.Member(element, attributes, !_perItemDrawing);
								}
							}

							if (gui.HasChanged())
							{
								if (_options.Readonly)
								{
									memberValue[i] = previous;
								}
								else if (_options.UniqueItems)
								{
									int occurances = 0;
									for (int k = 0; k < memberValue.Count; k++)
									{
										if (memberValue[i].GenericEquals(memberValue[k]))
										{
											occurances++;
											if (occurances > 1)
											{
												memberValue[i] = previous;
												break;
											}
										}
									}
								}
							}

							if (isAdvancedChecked)
							{
								var c = elementValue as Component;
								var go = c == null ? elementValue as GameObject : c.gameObject;
								if (go != null)
									gui.InspectButton(go);

								if (showAdvanced)
								{
									if (gui.MoveDownButton())
										MoveElementDown(i);
									if (gui.MoveUpButton())
										MoveElementUp(i);
								}
							}

							if (!_options.Readonly && _options.PerItemRemove && gui.RemoveButton("element", MiniButtonStyle.ModRight))
							{
								RemoveAt(i);
							}
						}
					}
#if PROFILE
					Profiler.EndSample();
#endif
				}
			}

			// footer
			if (_shouldDrawAddingArea)
			{
				Action<UnityObject> addOnDrop = obj =>
				{
					var go = obj as GameObject;
					object value;
					if (go != null)
					{
						value = _elementType == typeof(GameObject) ? (UnityObject)go : go.GetComponent(_elementType);
					}
					else value = obj;
					AddValue((TElement)value);
				};

				using (gui.Indent())
				{
					gui.DragDropArea<UnityObject>(
						@label: "+Drag-Drop+",
						@labelSize: 14,
						@style: EditorStyles.toolbarButton,
						@canSetVisualModeToCopy: dragObjects => dragObjects.All(obj =>
						{
							var go = obj as GameObject;
							var isGo = go != null;
							if (_elementType == typeof(GameObject))
							{
								return isGo;
							}
							return isGo ? go.GetComponent(_elementType) != null : obj.GetType().IsA(_elementType);
						}),
						@cursor: MouseCursor.Link,
						@onDrop: addOnDrop,
						@onMouseUp: () => SelectionWindow.Show(new Tab<UnityObject>(
							@getValues: () => UnityObject.FindObjectsOfType(_elementType),
							@getCurrent: () => null,
							@setTarget: item =>
							{
								AddValue((TElement)(object)item);
							},
							@getValueName: value => value.name,
							@title: _elementType.Name + "s")),
						@preSpace: 2f,
						@postSpace: 35f,
						@height: 15f
					);
				}
				gui.Space(3f);
			}
		}

		private EditorMember GetElement(int index)
		{
			if (index >= _elements.Count)
			{
				var element = EditorMember.WrapIListElement(
					@attributes  : attributes,
					@elementName : string.Empty,
                    @elementType : typeof(TElement),
					@elementId   : RuntimeHelper.CombineHashCodes(id, index)
				);

				element.InitializeIList(memberValue, index, rawTarget, unityTarget);
				_elements.Add(element);
				return element;
			}

			var e = _elements[index];
			e.InitializeIList(memberValue, index, rawTarget, unityTarget);
			return e;
		}

		// List ops
		#region
		protected abstract void Clear();
		protected abstract void RemoveAt(int atIndex);
		protected abstract void Insert(int index, TElement value);

		private void Shuffle()
		{
			memberValue.Shuffle();
		}

		private void MoveElementDown(int i)
		{
			memberValue.MoveElementDown(i);
		}

		private void MoveElementUp(int i)
		{
			memberValue.MoveElementUp(i);
		}

		private void RemoveLast()
		{
			RemoveAt(memberValue.Count - 1);
		}

		private void SetAt(int index, TElement value)
		{
			if (!memberValue[index].GenericEquals(value))
				memberValue[index] = value;
		}

		private void AddValue(TElement value)
		{
			Insert(memberValue.Count, value);
		}

		private void AddValue()
		{
			AddValue((TElement)_elementType.GetDefaultValueEmptyIfString());
		}
		#endregion

		private struct SequenceOptions
		{
			public bool Readonly;
			public bool Advanced;
			public bool LineNumbers;
			public bool PerItemRemove;
			public bool GuiBox;
			public bool UniqueItems;

			public SequenceOptions(Seq options)
			{
				Func<Seq, bool> contains = value =>
					(options & value) != 0;

				Readonly      = contains(Seq.Readonly);
				Advanced      = contains(Seq.Advanced);
				LineNumbers   = contains(Seq.LineNumbers);
				PerItemRemove = contains(Seq.PerItemRemove);
				GuiBox        = contains(Seq.GuiBox);
				UniqueItems   = contains(Seq.UniqueItems);
			}
		}
	}

	public class ArrayDrawer<T> : SequenceDrawer<T[], T>
	{
		protected override T[] GetNew()
		{
			return new T[0];
		}

		protected override void RemoveAt(int atIndex)
		{
			memberValue = memberValue.ToList().RemoveAtAndGet(atIndex).ToArray();
		}

		protected override void Clear()
		{
			memberValue = memberValue.ToList().ClearAndGet().ToArray();
		}

		protected override void Insert(int index, T value)
		{
			memberValue = memberValue.ToList().InsertAndGet(index, value).ToArray();
			foldout = true;
		}
	}

	public class ListDrawer<T> : SequenceDrawer<List<T>, T>
	{
		protected override List<T> GetNew()
		{
			return new List<T>();
		}

		protected override void RemoveAt(int index)
		{
			memberValue.RemoveAt(index);
		}

		protected override void Clear()
		{
			memberValue.Clear();
		}

		protected override void Insert(int index, T value)
		{
			memberValue.Insert(index, value);
			foldout = true;
		}
	}
}