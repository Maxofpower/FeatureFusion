'use client'

import { useRouter } from 'next/navigation'
import { useEffect, useMemo, useState } from 'react'
import { FormikErrors, useFormik } from 'formik'
import * as yup from 'yup'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Drawer,
  DrawerClose,
  DrawerContent,
  DrawerDescription,
  DrawerFooter,
  DrawerHeader,
  DrawerTitle,
  DrawerTrigger,
} from '@/components/ui/drawer'
import { SlidersHorizontal } from 'lucide-react'
import { useIsMobile } from '@/hooks/use-mobile'
import { FieldError } from '@/helper/field-error'
import {
  PRODUCT_DEFAULT_LIMIT,
  PRODUCT_MAX_LIMIT,
  PRODUCT_SORT_DIRECTIONS,
  PRODUCT_SORT_FIELDS,
  type ProductFilterValues,
} from '@/models'

interface Props {
  currentFilters: ProductFilterValues
}

type IFormState = ProductFilterValues

const DEFAULT_FILTERS: IFormState = {
  limit: PRODUCT_DEFAULT_LIMIT,
  sortBy: 'Id',
  sortDirection: 'Ascending',
}

const SORT_FIELD_LABELS: Record<(typeof PRODUCT_SORT_FIELDS)[number], string> = {
  Id: 'Id',
  Name: 'Name',
  Price: 'Price',
  CreatedAt: 'Created At',
  NameThenPrice: 'Name → Price',
}

const SORT_DIRECTION_LABELS: Record<(typeof PRODUCT_SORT_DIRECTIONS)[number], string> = {
  Ascending: 'Ascending',
  Descending: 'Descending',
}

export const ProductsFilters = ({ currentFilters }: Props) => {
  const router = useRouter()
  const isMobile = useIsMobile()
  const [open, setOpen] = useState(false)

  const initialValues: IFormState = {
    limit: currentFilters.limit,
    sortBy: currentFilters.sortBy,
    sortDirection: currentFilters.sortDirection,
  }

  const validationSchema = useMemo(
    () =>
      yup.object({
        limit: yup
          .number()
          .typeError('Must be a number')
          .integer('Must be a whole number')
          .min(1, 'Must be at least 1')
          .max(PRODUCT_MAX_LIMIT, `Must be at most ${PRODUCT_MAX_LIMIT}`)
          .required('This field is required'),
        sortBy: yup
          .string()
          .oneOf([...PRODUCT_SORT_FIELDS], 'Invalid sort field')
          .required('This field is required'),
        sortDirection: yup
          .string()
          .oneOf([...PRODUCT_SORT_DIRECTIONS], 'Invalid sort direction')
          .required('This field is required'),
      }),
    []
  )

  const actionSubmit = (values: IFormState) => {
    const params = new URLSearchParams({
      limit: values.limit,
      sortBy: values.sortBy,
      sortDirection: values.sortDirection,
    })
    // New query settings always restart from the first page (cursor is invalidated).
    router.push(`/products?${params.toString()}`)
    setOpen(false)
  }

  const formik = useFormik({
    initialValues,
    validationSchema,
    enableReinitialize: false,
    onSubmit: actionSubmit,
  })

  const formErrors: FormikErrors<IFormState> = {
    limit: formik.submitCount || formik.touched.limit ? formik.errors.limit : '',
    sortBy: formik.submitCount || formik.touched.sortBy ? formik.errors.sortBy : '',
    sortDirection:
      formik.submitCount || formik.touched.sortDirection ? formik.errors.sortDirection : '',
  }

  const handleDrawerChange = (nextOpen: boolean) => {
    setOpen(nextOpen)
    if (!nextOpen) {
      formik.resetForm()
    }
  }

  const resetFilters = () => {
    formik.resetForm({ values: DEFAULT_FILTERS })
    router.push('/products')
    setOpen(false)
  }

  useEffect(() => {
    if (open) {
      formik.setValues({
        limit: currentFilters.limit,
        sortBy: currentFilters.sortBy,
        sortDirection: currentFilters.sortDirection,
      })
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, currentFilters])

  return (
    <Drawer
      open={open}
      onOpenChange={handleDrawerChange}
      swipeDirection={isMobile ? 'down' : 'right'}
    >
      <DrawerTrigger render={<Button variant="outline" size="sm" />}>
        <SlidersHorizontal className="size-4" />
        Query Settings
      </DrawerTrigger>
      <DrawerContent>
        <form onSubmit={formik.handleSubmit} className="flex flex-col h-full">
          <DrawerHeader>
            <div className="flex items-center justify-between">
              <DrawerTitle>Products Query Settings</DrawerTitle>
              <Button variant="ghost" size="sm" type="button" onClick={resetFilters}>
                Reset
              </Button>
            </div>
            <DrawerDescription>
              Customize page size and sorting.
            </DrawerDescription>
          </DrawerHeader>

          <div className="flex flex-col gap-4 overflow-y-auto px-4 py-2">
            <div className="space-y-2">
              <Label htmlFor="limit" className="text-sm font-medium text-foreground">
                Limit
              </Label>
              <Input
                id="limit"
                name="limit"
                type="number"
                min={1}
                max={PRODUCT_MAX_LIMIT}
                value={formik.values.limit}
                onChange={formik.handleChange}
                onBlur={formik.handleBlur}
                aria-invalid={!!formErrors.limit}
              />
              <FieldError message={formErrors.limit} />
            </div>

            <div className="space-y-2">
              <Label htmlFor="sortBy" className="text-sm font-medium text-foreground">
                Sort By
              </Label>
              <Select
                value={formik.values.sortBy}
                onValueChange={(value) => formik.setFieldValue('sortBy', value ?? '')}
              >
                <SelectTrigger id="sortBy" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {PRODUCT_SORT_FIELDS.map((field) => (
                    <SelectItem key={field} value={field}>
                      {SORT_FIELD_LABELS[field]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <FieldError message={formErrors.sortBy} />
            </div>

            <div className="space-y-2">
              <Label htmlFor="sortDirection" className="text-sm font-medium text-foreground">
                Sort Direction
              </Label>
              <Select
                value={formik.values.sortDirection}
                onValueChange={(value) => formik.setFieldValue('sortDirection', value ?? '')}
              >
                <SelectTrigger id="sortDirection" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {PRODUCT_SORT_DIRECTIONS.map((direction) => (
                    <SelectItem key={direction} value={direction}>
                      {SORT_DIRECTION_LABELS[direction]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <FieldError message={formErrors.sortDirection} />
            </div>
          </div>

          <DrawerFooter>
            <div className="flex gap-2">
              <DrawerClose render={<Button variant="outline" type="button" className="flex-1" />} onClick={() => formik.resetForm()}>
                Cancel
              </DrawerClose>
              <Button type="submit" className="flex-1">
                Apply Filters
              </Button>
            </div>
          </DrawerFooter>
        </form>
      </DrawerContent>
    </Drawer>
  )
}
