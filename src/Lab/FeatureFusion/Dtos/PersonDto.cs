using FeatureFusion.Models.Validator;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System;
namespace FeatureFusion.Dtos
{
	public record PersonDto
	{
		public string Name { get; set; }
		public int Age { get; set; }
	};
	public class PersonDtoValidator : AbstractValidator<PersonDto>
	{
		public PersonDtoValidator()
		{
			RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
			RuleFor(x => x.Age).InclusiveBetween(18, 99).WithMessage("Age must be between 18 and 99.");
		}

	}

}
