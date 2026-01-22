// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
// Copyright (c) 2003-2018 Daiyuu Nobori.
// Copyright (c) 2013-2018 SoftEther VPN Project, University of Tsukuba, Japan.
// All Rights Reserved.
// 
// License: The Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// 
// THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
// THIS SOFTWARE IS DEVELOPED IN JAPAN, AND DISTRIBUTED FROM JAPAN, UNDER
// JAPANESE LAWS. YOU MUST AGREE IN ADVANCE TO USE, COPY, MODIFY, MERGE, PUBLISH,
// DISTRIBUTE, SUBLICENSE, AND/OR SELL COPIES OF THIS SOFTWARE, THAT ANY
// JURIDICAL DISPUTES WHICH ARE CONCERNED TO THIS SOFTWARE OR ITS CONTENTS,
// AGAINST US (IPA CYBERLAB, DAIYUU NOBORI, SOFTETHER VPN PROJECT OR OTHER
// SUPPLIERS), OR ANY JURIDICAL DISPUTES AGAINST US WHICH ARE CAUSED BY ANY KIND
// OF USING, COPYING, MODIFYING, MERGING, PUBLISHING, DISTRIBUTING, SUBLICENSING,
// AND/OR SELLING COPIES OF THIS SOFTWARE SHALL BE REGARDED AS BE CONSTRUED AND
// CONTROLLED BY JAPANESE LAWS, AND YOU MUST FURTHER CONSENT TO EXCLUSIVE
// JURISDICTION AND VENUE IN THE COURTS SITTING IN TOKYO, JAPAN. YOU MUST WAIVE
// ALL DEFENSES OF LACK OF PERSONAL JURISDICTION AND FORUM NON CONVENIENS.
// PROCESS MAY BE SERVED ON EITHER PARTY IN THE MANNER AUTHORIZED BY APPLICABLE
// LAW OR COURT RULE.

using System.Collections;
using System.Collections.Generic;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

// By Paraschiv Andrei Technical Blog
// http://paratechnical.blogspot.com/2011/03/radix-tree-implementation-in-c.html
// https://gist.githubusercontent.com/paratechnical/2869170/raw/79a8a56c04f5a3183b8ffe1f50dd200c4d6b68ef/C%23%20radix%20tree

namespace IPA.Cores.Basic;

/// <summary>
/// represents a node in the radix tree
/// stores:
/// a label - string that the tree holds
/// a list of the node's subnodes - a list of other objects of this type
/// </summary>
public class RadixNode
{
    // Serializable にするために残すこと
    public RadixNode()
    {
        this.Label = null!;
        this.SubNodes = null!;
    }

    public RadixNode(RadixNode? parent)
    {
        Label = new byte[0];
        SubNodes = new List<RadixNode>();

        this.Parent = parent;
    }

    public RadixNode(RadixNode? parent, byte[] l)
    {
        Label = l;
        SubNodes = new List<RadixNode>();

        this.Parent = parent;
    }
    public byte[] Label;
    public List<RadixNode> SubNodes;
    public RadixNode? Parent;
    public object? Object;

    public RadixNode? TraverseParentNonNull()
    {
        if (this.Object != null)
        {
            return this;
        }

        RadixNode? n = this.Parent;

        while (true)
        {
            if (n == null)
            {
                return null;
            }

            if (n.Object != null)
            {
                return n;
            }

            n = n.Parent;
        }
    }

    public override string? ToString()
    {
        if (this.Object != null) this.Object.ToString();
        return base.ToString();
    }
}

public class RadixTrie
{
    /// <summary>
    /// store the tree's root
    /// </summary>
    public RadixNode Root;

    public int Count = 0;

    /// <summary>
    /// construct a new tree with it's root
    /// </summary>
    public RadixTrie()
    {
        Root = new RadixNode(null, new byte[0]);
    }

    public RadixTrie(RadixNode root)
    {
        Root = root;
    }

    /// <summary>
    /// insert a word into the tree
    /// </summary>
    /// <param name="word"></param>
    public RadixNode? Insert(byte[] word)
    {
        return InsertRec(word, Root);
    }

    /// <summary>
    /// recursively traverse the tree
    /// carry the word you want inserted until a proper place for it is found and it can be inserted there
    /// if a node already stores a substring of the word(substrnig with the same first letter as the word itself)
    /// then that substring must be removed from the word and it's children checked out next
    /// hence the name wordPart - part of a word
    /// </summary>
    /// <param name="wordPart">the part of the word that is to be inserted that is not already included in any of the tree's nodes</param>
    /// <param name="curNode">the node currently traversed</param>
    private RadixNode? InsertRec(byte[] wordPart, RadixNode curNode)
    {
        RadixNode? ret = null;

        //get the number of characters that the word part that is to be inserted and the current node's label have
        //in common starting from the first position of both strings
        //matching characters in the two strings = have the same value at the same position in both strings
        int matches = MatchingConsecutiveCharacters(wordPart, 0, curNode);

        //if we are at the root node
        //OR
        //the number of characters from the two strings that match is
        //bigger than 0
        //smaller than the the part of the word that is to be inserted
        //bigger than the the label of the current node
        if ((matches == 0) || (curNode == Root) ||
            ((matches > 0) && (matches < wordPart.Length) && (matches >= curNode.Label.Length)))
        {
            //remove the current node's label from the word part
            bool inserted = false;
            byte[] newWordPart = Util.CopyByte(wordPart, matches);
            //search the node's subnodes and if the subnode label's first character matches 
            //the word part's first character then insert the word part after this node(call the
            //current method recursively)
            foreach (RadixNode child in curNode.SubNodes)
                if (child.Label[0] == newWordPart[0])
                {
                    inserted = true;
                    ret = InsertRec(newWordPart, child);
                }
            if (inserted == false)
            {
                ret = new RadixNode(curNode, newWordPart);
                curNode.SubNodes.Add(ret);
                this.Count++;
            }
        }
        else if (matches < wordPart.Length)
        {
            //in this case we have to nodes that we must add to the tree
            //one is the node that has a label extracted from the current node's label without the string of 
            //matching characters(common characters)
            //the other is the node that has it's label extracted from the current word part minus the string
            //of matching characters
            byte[] commonRoot = Util.CopyByte(wordPart, 0, matches);
            byte[] branchPreviousLabel = Util.CopyByte(curNode.Label, matches);
            byte[] branchNewLabel = Util.CopyByte(wordPart, matches);

            curNode.Label = commonRoot;

            RadixNode newNodePreviousLabel = new RadixNode(curNode, branchPreviousLabel);
            newNodePreviousLabel.Object = curNode.Object;
            newNodePreviousLabel.SubNodes.AddRange(curNode.SubNodes);

            foreach (RadixNode n in curNode.SubNodes)
            {
                n.Parent = newNodePreviousLabel;
            }

            curNode.SubNodes.Clear();
            curNode.SubNodes.Add(newNodePreviousLabel);
            curNode.Object = null;

            RadixNode newNodeNewLabel = new RadixNode(curNode, branchNewLabel);
            curNode.SubNodes.Add(newNodeNewLabel);
            ret = newNodeNewLabel;
            this.Count++;
        }
        else if (matches == curNode.Label.Length)
        {
            //in this case we don't do anything because the word is already added
        }
        else if (matches > curNode.Label.Length)
        {
            //add the current word part minus the common characters after the current node
            byte[] newNodeLabel = Util.CopyByte(curNode.Label, curNode.Label.Length, wordPart.Length);
            RadixNode newNode = new RadixNode(curNode, newNodeLabel);
            curNode.SubNodes.Add(newNode);
            ret = newNode;
            this.Count++;
        }

        return ret;
    }

    /// <summary>
    /// given a string and a node the number of characters that the string and the node's label have
    /// in common starting from the first character in each is returned
    /// </summary>
    /// <param name="word">a string that is to be compared with the node's label</param>
    /// <param name="curNode">a node</param>
    /// <returns></returns>
    private int MatchingConsecutiveCharacters(byte[] word, int word_pos, RadixNode curNode)
    {
        int matches = 0;
        int minLength = 0;

        //see which string is smaller and save it's lenght
        //when cycling throught the two strings we won't go any further than that
        if (curNode.Label.Length >= (word.Length - word_pos))
            minLength = word.Length - word_pos;
        else if (curNode.Label.Length < (word.Length - word_pos))
            minLength = curNode.Label.Length;

        if (minLength > 0)
            //go throught the two streams
            for (int i = 0; i < minLength; i++)
            {
                //if two characters at the same position have the same value we have one more match
                if (word[i + word_pos] == curNode.Label[i])
                    matches++;
                else
                    //if at any position the two strings have different characters break the cycle
                    break;
            }
        //and return the current number of matches
        return matches;
    }

    public RadixNode? Lookup(byte[] word)
    {
        return LookupRec(word, 0, Root);
    }

    /// <summary>
    /// look for a word in the tree begining at the current node 
    /// </summary>
    /// <param name="wordPart"></param>
    /// <param name="curNode"></param>
    /// <returns></returns>
    private RadixNode? LookupRec(byte[] word, int pos, RadixNode curNode)
    {
        int matches = MatchingConsecutiveCharacters(word, pos, curNode);

        if ((matches == 0) || (curNode == Root) ||
            ((matches > 0) && (matches < (word.Length - pos)) && (matches >= curNode.Label.Length)))
        {
            RadixNode? ret = null;
            int new_pos = pos + matches;
            foreach (RadixNode child in curNode.SubNodes)
            {
                if (child.Label[0] == word[new_pos])
                {
                    ret = LookupRec(word, new_pos, child);
                    if (ret != null)
                    {
                        break;
                    }
                }
            }

            if (ret == null)
            {
                ret = curNode;
            }

            return ret;
        }
        else if (matches == curNode.Label.Length)
        {
            return curNode;
        }
        else return null;
    }

    void enum_objects(ArrayList o, RadixNode n)
    {
        if (n.Object != null)
        {
            o.Add(n.Object);
        }

        foreach (RadixNode nc in n.SubNodes)
        {
            enum_objects(o, nc);
        }
    }

    public ArrayList EnumAllObjects()
    {
        ArrayList ret = new ArrayList();

        enum_objects(ret, this.Root);

        return ret;
    }
}

/// <summary>
/// represents a node in the radix tree
/// stores:
/// a label - string that the tree holds
/// a list of the node's subnodes - a list of other objects of this type
/// </summary>
public class RadixNode<T> where T : class
{
    // Serializable にするため消さないこと
    public RadixNode()
    {
        this.Label = null!;
        this.SubNodes = null!;
        this.Parent = null!;
    }

    public RadixNode(RadixNode<T>? parent)
    {
        Label = new byte[0];
        SubNodes = new List<RadixNode<T>>();

        this.Parent = parent;
    }

    public RadixNode(RadixNode<T>? parent, byte[] l)
    {
        Label = l;
        SubNodes = new List<RadixNode<T>>();

        this.Parent = parent;
    }
    public byte[] Label;
    public List<RadixNode<T>> SubNodes;
    public RadixNode<T>? Parent;
    public T? Object;

    public RadixNode<T>? TraverseParentNonNull()
    {
        if (this.Object != null)
        {
            return this;
        }

        RadixNode<T>? n = this.Parent;

        while (true)
        {
            if (n == null)
            {
                return null;
            }

            if (n.Object != null)
            {
                return n;
            }

            n = n.Parent;
        }
    }

    public override string? ToString()
    {
        if (this.Object != null) this.Object.ToString();
        return base.ToString();
    }
}


public class RadixTrie<T> where T : class
{
    /// <summary>
    /// store the tree's root
    /// </summary>
    public RadixNode<T> Root;

    public int Count = 0;

    /// <summary>
    /// construct a new tree with it's root
    /// </summary>
    public RadixTrie()
    {
        Root = new RadixNode<T>(null, new byte[0]);
    }

    public RadixTrie(RadixNode<T> root)
    {
        Root = root;
    }

    /// <summary>
    /// insert a word into the tree
    /// </summary>
    /// <param name="word"></param>
    public RadixNode<T>? Insert(byte[] word)
    {
        return InsertRec(word, Root);
    }

    /// <summary>
    /// recursively traverse the tree
    /// carry the word you want inserted until a proper place for it is found and it can be inserted there
    /// if a node already stores a substring of the word(substrnig with the same first letter as the word itself)
    /// then that substring must be removed from the word and it's children checked out next
    /// hence the name wordPart - part of a word
    /// </summary>
    /// <param name="wordPart">the part of the word that is to be inserted that is not already included in any of the tree's nodes</param>
    /// <param name="curNode">the node currently traversed</param>
    private RadixNode<T>? InsertRec(byte[] wordPart, RadixNode<T> curNode)
    {
        RadixNode<T>? ret = null;

        //get the number of characters that the word part that is to be inserted and the current node's label have
        //in common starting from the first position of both strings
        //matching characters in the two strings = have the same value at the same position in both strings
        int matches = MatchingConsecutiveCharacters(wordPart, 0, curNode);

        //if we are at the root node
        //OR
        //the number of characters from the two strings that match is
        //bigger than 0
        //smaller than the the part of the word that is to be inserted
        //bigger than the the label of the current node
        if ((matches == 0) || (curNode == Root) ||
            ((matches > 0) && (matches < wordPart.Length) && (matches >= curNode.Label.Length)))
        {
            //remove the current node's label from the word part
            bool inserted = false;
            byte[] newWordPart = Util.CopyByte(wordPart, matches);
            //search the node's subnodes and if the subnode label's first character matches 
            //the word part's first character then insert the word part after this node(call the
            //current method recursively)
            foreach (RadixNode<T> child in curNode.SubNodes)
                if (child.Label[0] == newWordPart[0])
                {
                    inserted = true;
                    ret = InsertRec(newWordPart, child);
                }
            if (inserted == false)
            {
                ret = new RadixNode<T>(curNode, newWordPart);
                curNode.SubNodes.Add(ret);
                this.Count++;
            }
        }
        else if (matches < wordPart.Length)
        {
            //in this case we have to nodes that we must add to the tree
            //one is the node that has a label extracted from the current node's label without the string of 
            //matching characters(common characters)
            //the other is the node that has it's label extracted from the current word part minus the string
            //of matching characters
            byte[] commonRoot = Util.CopyByte(wordPart, 0, matches);
            byte[] branchPreviousLabel = Util.CopyByte(curNode.Label, matches);
            byte[] branchNewLabel = Util.CopyByte(wordPart, matches);

            curNode.Label = commonRoot;

            RadixNode<T> newNodePreviousLabel = new RadixNode<T>(curNode, branchPreviousLabel);
            newNodePreviousLabel.Object = curNode.Object;
            newNodePreviousLabel.SubNodes.AddRange(curNode.SubNodes);

            foreach (RadixNode<T> n in curNode.SubNodes)
            {
                n.Parent = newNodePreviousLabel;
            }

            curNode.SubNodes.Clear();
            curNode.SubNodes.Add(newNodePreviousLabel);
            curNode.Object = default(T);

            RadixNode<T> newNodeNewLabel = new RadixNode<T>(curNode, branchNewLabel);
            curNode.SubNodes.Add(newNodeNewLabel);
            ret = newNodeNewLabel;
            this.Count++;
        }
        else if (matches == curNode.Label.Length)
        {
            //in this case we don't do anything because the word is already added
        }
        else if (matches > curNode.Label.Length)
        {
            //add the current word part minus the common characters after the current node
            byte[] newNodeLabel = Util.CopyByte(curNode.Label, curNode.Label.Length, wordPart.Length);
            RadixNode<T> newNode = new RadixNode<T>(curNode, newNodeLabel);
            curNode.SubNodes.Add(newNode);
            ret = newNode;
            this.Count++;
        }

        return ret;
    }

    /// <summary>
    /// given a string and a node the number of characters that the string and the node's label have
    /// in common starting from the first character in each is returned
    /// </summary>
    /// <param name="word">a string that is to be compared with the node's label</param>
    /// <param name="curNode">a node</param>
    /// <returns></returns>
    private int MatchingConsecutiveCharacters(byte[] word, int wordPos, RadixNode<T> curNode)
    {
        int matches = 0;
        int minLength = 0;

        //see which string is smaller and save it's lenght
        //when cycling throught the two strings we won't go any further than that
        if (curNode.Label.Length >= (word.Length - wordPos))
            minLength = word.Length - wordPos;
        else if (curNode.Label.Length < (word.Length - wordPos))
            minLength = curNode.Label.Length;

        if (minLength > 0)
            //go throught the two streams
            for (int i = 0; i < minLength; i++)
            {
                //if two characters at the same position have the same value we have one more match
                if (word[i + wordPos] == curNode.Label[i])
                    matches++;
                else
                    //if at any position the two strings have different characters break the cycle
                    break;
            }
        //and return the current number of matches
        return matches;
    }

    public RadixNode<T>? Lookup(byte[] word)
    {
        return LookupRec(word, 0, Root);
    }

    /// <summary>
    /// look for a word in the tree begining at the current node 
    /// </summary>
    /// <param name="wordPart"></param>
    /// <param name="curNode"></param>
    /// <returns></returns>
    private RadixNode<T>? LookupRec(byte[] word, int pos, RadixNode<T> curNode)
    {
        int matches = MatchingConsecutiveCharacters(word, pos, curNode);

        if ((matches == 0) || (curNode == Root) ||
            ((matches > 0) && (matches < (word.Length - pos)) && (matches >= curNode.Label.Length)))
        {
            RadixNode<T>? ret = null;
            int new_pos = pos + matches;
            foreach (RadixNode<T> child in curNode.SubNodes)
            {
                if (child.Label[0] == word[new_pos])
                {
                    ret = LookupRec(word, new_pos, child);
                    if (ret != null)
                    {
                        break;
                    }
                }
            }

            if (ret == null)
            {
                ret = curNode;
            }

            return ret;
        }
        else if (matches == curNode.Label.Length)
        {
            return curNode;
        }
        else return null;
    }

    void enum_objects(ArrayList o, RadixNode<T> n)
    {
        if (n.Object != null)
        {
            o.Add(n.Object);
        }

        foreach (RadixNode<T> nc in n.SubNodes)
        {
            enum_objects(o, nc);
        }
    }

    public ArrayList EnumAllObjects()
    {
        ArrayList ret = new ArrayList();

        enum_objects(ret, this.Root);

        return ret;
    }
}
