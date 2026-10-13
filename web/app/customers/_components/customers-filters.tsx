'use client'

import { useRouter } from 'next/navigation'
import { useEffect, useMemo, useState } from 'react'
import { FormikErrors, useFormik } from 'formik'
import * as yup from 'yup'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
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
  CUSTOMER_DEFAULT_LIMIT,
  CUSTOMER_MAX_LIMIT,
  type CustomerFilterValues,
} from '@/models'

interface Props {
  currentFilters: CustomerFilterValues
}

type IFormState = CustomerFilterValues

const DEFAULT_FILTERS: IFormState = {
  limit: CUSTOMER_DEFAULT_LIMIT,
}

export const CustomersFilters = ({ currentFilters }: Props) => {
  const router = useRouter()
  const isMobile = useIsMobile()
  const [open, setOpen] = useState(false)

  const initialValues: IFormState = {
    limit: currentFilters.limit,
  }

  const validationSchema = useMemo(
    () =>
      yup.object({
        limit: yup
          .number()
          .typeError('Must be a number')
          .integer('Must be a whole number')
          .min(1, 'Must be at least 1')
          .max(CUSTOMER_MAX_LIMIT, `Must be at most ${CUSTOMER_MAX_LIMIT}`)
          .required('This field is required'),
      }),
    []
  )

  const actionSubmit = (values: IFormState) => {
    const params = new URLSearchParams({ limit: values.limit })
    // New query settings always restart from the first page (cursor is invalidated).
    router.push(`/customers?${params.toString()}`)
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
  }

  const handleDrawerChange = (nextOpen: boolean) => {
    setOpen(nextOpen)
    if (!nextOpen) {
      formik.resetForm()
    }
  }

  const resetFilters = () => {
    formik.resetForm({ values: DEFAULT_FILTERS })
    router.push('/customers')
    setOpen(false)
  }

  useEffect(() => {
    if (open) {
      formik.setValues({
        limit: currentFilters.limit,
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
              <DrawerTitle>Customers Query Settings</DrawerTitle>
              <Button variant="ghost" size="sm" type="button" onClick={resetFilters}>
                Reset
              </Button>
            </div>
            <DrawerDescription>
              Customize page size. Customers are always newest first.
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
                max={CUSTOMER_MAX_LIMIT}
                value={formik.values.limit}
                onChange={formik.handleChange}
                onBlur={formik.handleBlur}
                aria-invalid={!!formErrors.limit}
              />
              <FieldError message={formErrors.limit} />
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
