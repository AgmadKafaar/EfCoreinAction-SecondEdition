﻿// Copyright (c) 2019 Jon P Smith, GitHub: JonPSmith, web: http://www.thereformedprogrammer.net/
// Licensed under MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BookApp.Domain.Book.DomainEvents;
using GenericEventRunner.DomainParts;
using StatusGeneric;

namespace BookApp.Domain.Book
{
    public class Book : EntityEventsBase
    {
        public const int PromotionalTextLength = 200;
        private HashSet<BookAuthor> _authorsLink;

        //-----------------------------------------------
        //relationships

        //Use uninitialized backing fields - this means we can detect if the collection was loaded
        private HashSet<Review> _reviews;

        //-----------------------------------------------
        //ctors

        private Book() { } //Needed by EF Core

        [Required(AllowEmptyStrings = false)]
        public string Title { get; private set; }

        public string Description { get; private set; }
        public DateTime PublishedOn { get; set; }
        public string Publisher { get; private set; }
        public decimal OrgPrice { get; private set; }
        public decimal ActualPrice { get; private set; }

        [MaxLength(PromotionalTextLength)]
        public string PromotionalText { get; private set; }

        public string ImageUrl { get; private set; }

        public bool SoftDeleted { get; private set; }

        public IEnumerable<Review> Reviews => _reviews?.ToList();
        public IEnumerable<BookAuthor> AuthorsLink => _authorsLink?.ToList();

        public Guid BookId { get; private set; }

        //----------------------------------------------
        //Extra properties filled in by events

        [ConcurrencyCheck]
        public string AuthorsOrdered { get; set; }

        [ConcurrencyCheck]
        public int ReviewsCount { get; set; }

        [ConcurrencyCheck]
        public double ReviewsAverageVotes { get; set; }

        //This is an action provided in the review add/remove event so that the review handler can update these properties
        private void UpdateReviewCachedValues(int reviewsCount, double reviewsAverageVotes)
        {
            ReviewsCount = reviewsCount;
            ReviewsAverageVotes = reviewsAverageVotes;
        }
        //----------------------------------------------

        public static IStatusGeneric<Book> CreateBook(string title, string description, DateTime publishedOn,
            string publisher, decimal price, string imageUrl, ICollection<Author> authors)
        {
            var status = new StatusGenericHandler<Book>();
            if (string.IsNullOrWhiteSpace(title))
                status.AddError("The book title cannot be empty.");

            var book = new Book
            {
                BookId = Guid.NewGuid(),
                Title = title,
                Description = description,
                PublishedOn = publishedOn,
                Publisher = publisher,
                ActualPrice = price,
                OrgPrice = price,
                ImageUrl = imageUrl,
                //We need to initialise the AuthorsOrdered string when the entry is created
                AuthorsOrdered = string.Join(", ", authors.Select(x => x.Name)),
                //We don't need to initialise the ReviewsCount and the ReviewsAverageVotes  as they default to zero
                _reviews = new HashSet<Review>()       //We add an empty list on create. I allows reviews to be added when building test data
            };
            if (authors == null)
                throw new ArgumentNullException(nameof(authors));

            byte order = 0;
            book._authorsLink = new HashSet<BookAuthor>(authors.Select(a => new BookAuthor(book, a, order++)));
            if (!book._authorsLink.Any())
                status.AddError("You must have at least one Author for a book.");

            return status.SetResult(book);
        }

        public void UpdatePublishedOn(DateTime publishedOn)
        {
            PublishedOn = publishedOn;
        }

        //This works with the GenericServices' IncludeThen Attribute to pre-load the Reviews collection
        public void AddReview(int numStars, string comment, string voterName)
        {
            if (_reviews == null)
                throw new InvalidOperationException("The Reviews collection must be loaded before calling this method");
            _reviews.Add(new Review(numStars, comment, voterName));
        }

        //This works with the GenericServices' IncludeThen Attribute to pre-load the Reviews collection
        public void RemoveReview(int reviewId)
        {
            if (_reviews == null)
                throw new InvalidOperationException("The Reviews collection must be loaded before calling this method");
            var localReview = _reviews.SingleOrDefault(x => x.ReviewId == reviewId);
            if (localReview == null)
                throw new InvalidOperationException("The review with that key was not found in the book's Reviews.");
            _reviews.Remove(localReview);
            AddEvent(new BookReviewRemovedEvent(localReview, this, UpdateReviewCachedValues));
        }

        public IStatusGeneric AddPromotion(decimal actualPrice, string promotionalText)                  
        {
            var status = new StatusGenericHandler();
            if (string.IsNullOrWhiteSpace(promotionalText))
            {
                status.AddError("You must provide some text to go with the promotion.", nameof(PromotionalText));
                return status;
            }

            ActualPrice = actualPrice;  
            PromotionalText = promotionalText;

            status.Message = $"The book's new price is ${actualPrice:F}.";

            return status; 
        }

        public void RemovePromotion() 
        {
            ActualPrice = OrgPrice; 
            PromotionalText = null; 
        }
    }

}