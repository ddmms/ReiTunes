﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ReiTunes.Core {
    // Based on CDillinger's Gist from https://gist.github.com/CDillinger/2aa02128f840bdca90340ce08ee71bc2
    // LICENSE
    //
    //   This software is dual-licensed to the public domain and under the following
    //   license: you are granted a perpetual, irrevocable license to copy, modify,
    //   publish, and distribute this file as you see fit.

    public static class FuzzyMatcher {

        public static ObservableCollection<LibraryItem> FuzzyMatch(string filterString, IEnumerable<LibraryItem> items) {
            if (string.IsNullOrWhiteSpace(filterString)) {
                return new ObservableCollection<LibraryItem>(items);
            }
            else {
                var fuzzyMatchResults =
                    (from item in items
                     let fuzzyResult = FuzzyMatcher.FuzzyMatch(item.AllSearchProperties, filterString)
                     where fuzzyResult.isMatch
                     orderby fuzzyResult.score descending
                     select item);

                //TODO: short-circuit if the result hasn't changed, to avoid slow rerenders.
                return new ObservableCollection<LibraryItem>(fuzzyMatchResults);
            }
        }

        /// <summary>
        /// Does a fuzzy search for a pattern within a string, and gives the search a score on how well it matched.
        /// </summary>
        /// <param name="stringToSearch">The string to search for the pattern in.</param>
        /// <param name="pattern">The pattern to search for in the string.</param>
        /// <returns>true if each character in pattern is found sequentially within stringToSearch; otherwise, false.</returns>
        public static (bool isMatch, int score) FuzzyMatch(string stringToSearch, string pattern) {
            // Score consts
            const int adjacencyBonus = 20;               // bonus for adjacent matches
            const int separatorBonus = 20;              // bonus if match occurs after a separator
            const int camelBonus = 10;                  // bonus if match is uppercase and prev is lower

            const int leadingLetterPenalty = -3;        // penalty applied for every letter in stringToSearch before the first match
            const int maxLeadingLetterPenalty = -9;     // maximum penalty for leading letters
            const int unmatchedLetterPenalty = 0;      // penalty for every letter that doesn't matter

            // Loop variables
            var score = 0;
            var patternIdx = 0;
            var patternLength = pattern.Length;
            var strIdx = 0;
            var strLength = stringToSearch.Length;
            int prevMatched = 0;
            var prevLower = false;
            var prevSeparator = true;                   // true if first letter match gets separator bonus

            // Use "best" matched letter if multiple string letters match the pattern
            char? bestLetter = null;
            char? bestLower = null;
            int? bestLetterIdx = null;
            var bestLetterScore = 0;

            var matchedIndices = new List<int>();

            // Loop over strings
            while (strIdx != strLength) {
                var patternChar = patternIdx != patternLength ? pattern[patternIdx] as char? : null;
                var strChar = stringToSearch[strIdx];

                var patternLower = patternChar != null ? char.ToLower((char)patternChar) as char? : null;
                var strLower = char.ToLower(strChar);
                var strUpper = char.ToUpper(strChar);

                var nextMatch = patternChar != null && patternLower == strLower;
                var rematch = bestLetter != null && bestLower == strLower;

                var advanced = nextMatch && bestLetter != null;
                var patternRepeat = bestLetter != null && patternChar != null && bestLower == patternLower;
                if (advanced || patternRepeat) {
                    score += bestLetterScore;
                    matchedIndices.Add((int)bestLetterIdx);
                    bestLetter = null;
                    bestLower = null;
                    bestLetterIdx = null;
                    bestLetterScore = 0;
                }

                if (nextMatch || rematch) {
                    var newScore = 0;

                    // Apply penalty for each letter before the first pattern match
                    // Note: Math.Max because penalties are negative values. So max is smallest penalty.
                    if (patternIdx == 0) {
                        var penalty = Math.Max(strIdx * leadingLetterPenalty, maxLeadingLetterPenalty);
                        score += penalty;
                    }

                    // Apply increasing bonus for consecutive bonuses
                    if (prevMatched > 0)
                        newScore += adjacencyBonus * prevMatched;

                    // Apply bonus for matches after a separator
                    if (prevSeparator)
                        newScore += separatorBonus;

                    // Apply bonus across camel case boundaries. Includes "clever" isLetter check.
                    if (prevLower && strChar == strUpper && strLower != strUpper)
                        newScore += camelBonus;

                    // Update pattern index IF the next pattern letter was matched
                    if (nextMatch)
                        ++patternIdx;

                    // Update best letter in stringToSearch which may be for a "next" letter or a "rematch"
                    if (newScore >= bestLetterScore) {
                        // Apply penalty for now skipped letter
                        if (bestLetter != null)
                            score += unmatchedLetterPenalty;

                        bestLetter = strChar;
                        bestLower = char.ToLower((char)bestLetter);
                        bestLetterIdx = strIdx;
                        bestLetterScore = newScore;
                    }

                    prevMatched++;
                }
                else {
                    score += unmatchedLetterPenalty;
                    prevMatched = 0;
                }

                // Includes "clever" isLetter check.
                prevLower = strChar == strLower && strLower != strUpper;
                prevSeparator = strChar == '_' || strChar == ' ';

                ++strIdx;
            }

            // Apply score for last match
            if (bestLetter != null) {
                score += bestLetterScore;
                matchedIndices.Add((int)bestLetterIdx);
            }

            return (patternIdx == patternLength, score);
        }
    }
}